using CodebaseIndexer.Application.BuildDeps;
using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Domain.Embedding;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Services;

/// <summary>Port of Python map_service_dependencies.</summary>
public sealed class ServiceMapService : IServiceMapService
{
    private static readonly string[] DiscoveryQueries =
    [
        "REST controller endpoint mapping route RequestMapping GetMapping PostMapping",
        "@RestController @RequestMapping @GetMapping @PostMapping @PutMapping @DeleteMapping",
        "RestTemplate exchange getForObject postForObject getForEntity",
        "WebClient create builder retrieve bodyToMono bodyToFlux",
        "HTTP client RestTemplate WebClient HttpClient base URL config",
        "@FeignClient Feign client interface",
        "application.yml application.properties config host url endpoint",
        "base URL host address service connection configuration",
        "Feign client service connector proxy",
        "adapter service operation request response",
        "pom.xml dependency groupId artifactId version maven parent",
        "csproj PackageReference ProjectReference NuGet dotnet",
        "package.json dependencies devDependencies npm",
        "build.gradle implementation api dependency compile",
        "go.mod require module golang",
        "Cargo.toml dependencies crate rust",
        "pyproject.toml requirements.txt dependencies python",
    ];

    private readonly IVectorStore _store;
    private readonly IDenseEmbedder _dense;
    private readonly ISparseEmbedder _sparse;
    private readonly EmbeddingOptions _embedding;
    private readonly DiscoveryOptions _discovery;
    private readonly UrlExtractors _extractors;

    /// <summary>Creates the service-map use-case.</summary>
    public ServiceMapService(
        IVectorStore store,
        [FromKeyedServices(EmbedderBackendKeys.Dense.Tei)] IDenseEmbedder dense,
        [FromKeyedServices(EmbedderBackendKeys.Sparse.Onnx)] ISparseEmbedder sparse,
        IOptions<EmbeddingOptions> embedding,
        IOptions<DiscoveryOptions> discovery,
        UrlExtractors extractors)
    {
        _store = store;
        _dense = dense;
        _sparse = sparse;
        _embedding = embedding.Value;
        _discovery = discovery.Value;
        _extractors = extractors;
    }

    /// <inheritdoc />
    public async Task<object> MapServiceDependenciesAsync(
        IReadOnlyList<string>? collections = null,
        int topK = 30,
        bool? rerank = null,
        CancellationToken cancellationToken = default)
    {
        _ = rerank;
        IReadOnlyList<string> targetCollections;
        if (collections is { Count: > 0 })
        {
            targetCollections = collections;
        }
        else
        {
            var stats = await _store.ListCollectionStatsAsync(cancellationToken).ConfigureAwait(false);
            targetCollections = stats.Select(s => s.Name).ToArray();
        }

        if (targetCollections.Count < 2)
        {
            return new Dictionary<string, object?>
            {
                ["error"] = "Need at least 2 indexed collections to map dependencies.",
                ["collections_found"] = targetCollections,
            };
        }

        var queries = DiscoveryQueries.Concat(_discovery.ServiceDiscoveryExtraQueryList).ToArray();
        var denseVectors = await _dense.EmbedQueryAsync(queries, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparseVector>? sparseVectors = null;
        if (_embedding.HybridSearch)
        {
            sparseVectors = await _sparse.EmbedBatchAsync(queries, cancellationToken).ConfigureAwait(false);
        }

        var endpointsByColl = new Dictionary<string, List<ServiceMapChunk>>(StringComparer.Ordinal);
        var callersByColl = new Dictionary<string, List<ServiceMapChunk>>(StringComparer.Ordinal);
        var configsByColl = new Dictionary<string, List<ServiceMapChunk>>(StringComparer.Ordinal);
        var manifestsByColl = new Dictionary<string, List<ServiceMapChunk>>(StringComparer.Ordinal);
        var seenChunks = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < queries.Length; i++)
        {
            var dense = denseVectors[i];
            SparseVector? sparse = sparseVectors is not null ? sparseVectors[i] : null;
            var tasks = targetCollections.Select(coll =>
                _store.SearchAsync(coll, dense, sparse, topK, language: null, minScore: 0.25f, cancellationToken));
            var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
            var hits = batches.SelectMany(b => b).ToArray();
            if (targetCollections.Count > 1)
            {
                var perCollection = batches
                    .Where(b => b.Count > 0)
                    .Cast<IReadOnlyList<SearchHit>>()
                    .ToArray();
                hits = perCollection.Length > 1
                    ? CrossCollectionRrf.Fuse(perCollection, _embedding.RrfK, topK).ToArray()
                    : hits.Take(topK).ToArray();
            }

            foreach (var r in hits)
            {
                var chunkKey = $"{r.Collection}:{r.RelPath}:{r.StartLine}";
                if (!seenChunks.Add(chunkKey))
                {
                    continue;
                }

                var entry = new ServiceMapChunk(
                    r.RelPath, r.SymbolName, r.SymbolType, r.StartLine, r.EndLine, r.Language,
                    r.Content.Length > 300 ? r.Content[..300] : r.Content,
                    r.Content);

                var routePaths = UrlExtractors.RoutePaths(r.Content, r.RelPath);
                if (routePaths.Count > 0)
                {
                    entry.Routes = routePaths;
                    Add(endpointsByColl, r.Collection, entry);
                    continue;
                }

                var codeUrls = _extractors.CodeUrls(r.Content);
                var (configPaths, baseUrls) = _extractors.ConfigUrls(r.Content);
                if (baseUrls.Count > 0 || configPaths.Count > 0)
                {
                    entry.BaseUrls = baseUrls;
                    entry.ConfigPaths = configPaths;
                    Add(configsByColl, r.Collection, entry);
                }
                else if (codeUrls.Count > 0)
                {
                    entry.CalledPaths = codeUrls;
                    Add(callersByColl, r.Collection, entry);
                }
                else if (BuildManifestDetector.IsBuildManifest(r.RelPath))
                {
                    Add(manifestsByColl, r.Collection, entry);
                }
            }
        }

        var edges = new List<ServiceMapEdge>();
        var seenEdges = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (callerColl, callerEntries) in callersByColl)
        {
            foreach (var (epColl, epEntries) in endpointsByColl)
            {
                if (callerColl == epColl)
                {
                    continue;
                }

                foreach (var caller in callerEntries)
                {
                    foreach (var endpoint in epEntries)
                    {
                        var matched = new SortedSet<string>(StringComparer.Ordinal);
                        foreach (var cp in caller.CalledPaths ?? Array.Empty<string>())
                        {
                            foreach (var ep in endpoint.Routes ?? Array.Empty<string>())
                            {
                                if (UrlExtractors.PathsMatch(cp, ep))
                                {
                                    matched.Add($"{cp} → {ep}");
                                }
                            }
                        }

                        if (matched.Count == 0)
                        {
                            continue;
                        }

                        var edgeKey = $"{callerColl}:{caller.RelPath}→{epColl}:{endpoint.RelPath}";
                        if (!seenEdges.Add(edgeKey))
                        {
                            continue;
                        }

                        edges.Add(new ServiceMapEdge(
                            "http_call", callerColl, caller.RelPath, caller.SymbolName,
                            epColl, endpoint.RelPath, endpoint.SymbolName, matched.ToArray()));
                    }
                }
            }
        }

        foreach (var (cfgColl, cfgEntries) in configsByColl)
        {
            foreach (var (epColl, epEntries) in endpointsByColl)
            {
                if (cfgColl == epColl)
                {
                    continue;
                }

                foreach (var cfg in cfgEntries)
                {
                    foreach (var endpoint in epEntries)
                    {
                        var matched = new SortedSet<string>(StringComparer.Ordinal);
                        foreach (var cp in cfg.ConfigPaths ?? Array.Empty<string>())
                        {
                            foreach (var ep in endpoint.Routes ?? Array.Empty<string>())
                            {
                                if (UrlExtractors.PathsMatch(cp, ep))
                                {
                                    matched.Add($"{cp} → {ep}");
                                }
                            }
                        }

                        if (matched.Count == 0)
                        {
                            continue;
                        }

                        var edgeKey = $"{cfgColl}:{cfg.RelPath}→{epColl}:{endpoint.RelPath}";
                        if (!seenEdges.Add(edgeKey))
                        {
                            continue;
                        }

                        edges.Add(new ServiceMapEdge(
                            "config_reference", cfgColl, cfg.RelPath, null,
                            epColl, endpoint.RelPath, endpoint.SymbolName, matched.ToArray(),
                            cfg.BaseUrls));
                    }
                }
            }
        }

        var buildDepEdges = new List<ServiceMapEdge>();
        var seenBuild = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (depColl, manifestEntries) in manifestsByColl)
        {
            var contentByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in manifestEntries)
            {
                contentByPath[entry.RelPath] = contentByPath.GetValueOrDefault(entry.RelPath, "") + "\n" + entry.FullContent;
            }

            foreach (var (relPath, merged) in contentByPath)
            {
                var deps = BuildDepExtractor.Extract(merged, relPath);
                var matches = BuildDepCollectionMatcher.Match(deps, targetCollections, depColl);
                foreach (var m in matches)
                {
                    var edgeKey = $"{depColl}:{relPath}→{m.MatchedCollection}:{m.Artifact}";
                    if (!seenBuild.Add(edgeKey))
                    {
                        continue;
                    }

                    buildDepEdges.Add(new ServiceMapEdge(
                        "build_dependency", depColl, relPath, null,
                        m.MatchedCollection, null, null, null, null,
                        m.Artifact, m.Group, m.Version, m.Scope, m.Ecosystem, m.MatchConfidence));
                }
            }
        }

        edges.AddRange(buildDepEdges);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!adjacency.TryGetValue(edge.FromService, out var list))
            {
                list = [];
                adjacency[edge.FromService] = list;
            }

            if (!list.Contains(edge.ToService, StringComparer.Ordinal))
            {
                list.Add(edge.ToService);
            }
        }

        var services = new Dictionary<string, ServiceMapServiceSummary>(StringComparer.Ordinal);
        foreach (var coll in targetCollections)
        {
            services[coll] = new ServiceMapServiceSummary(
                endpointsByColl.GetValueOrDefault(coll)?.Count ?? 0,
                callersByColl.GetValueOrDefault(coll)?.Count ?? 0,
                configsByColl.GetValueOrDefault(coll)?.Count ?? 0,
                manifestsByColl.GetValueOrDefault(coll)?.Count ?? 0,
                buildDepEdges.Count(e => e.FromService == coll),
                adjacency.GetValueOrDefault(coll) ?? [],
                adjacency.Where(kv => kv.Value.Contains(coll)).Select(kv => kv.Key).ToArray());
        }

        return new ServiceMapResponse(
            targetCollections,
            services,
            adjacency.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal),
            edges,
            new ServiceMapSummary(
                endpointsByColl.Values.Sum(v => v.Count),
                callersByColl.Values.Sum(v => v.Count),
                configsByColl.Values.Sum(v => v.Count),
                buildDepEdges.Count,
                edges.Count));
    }

    private static void Add(Dictionary<string, List<ServiceMapChunk>> map, string coll, ServiceMapChunk entry)
    {
        if (!map.TryGetValue(coll, out var list))
        {
            list = [];
            map[coll] = list;
        }

        list.Add(entry);
    }

    private sealed class ServiceMapChunk
    {
        public ServiceMapChunk(
            string relPath, string? symbolName, string symbolType, int startLine, int endLine,
            string language, string contentPreview, string fullContent)
        {
            RelPath = relPath;
            SymbolName = symbolName;
            SymbolType = symbolType;
            StartLine = startLine;
            EndLine = endLine;
            Language = language;
            ContentPreview = contentPreview;
            FullContent = fullContent;
        }

        public string RelPath { get; }
        public string? SymbolName { get; }
        public string SymbolType { get; }
        public int StartLine { get; }
        public int EndLine { get; }
        public string Language { get; }
        public string ContentPreview { get; }
        public string FullContent { get; }
        public IReadOnlyList<string>? Routes { get; set; }
        public IReadOnlyList<string>? CalledPaths { get; set; }
        public IReadOnlyList<string>? ConfigPaths { get; set; }
        public IReadOnlyList<string>? BaseUrls { get; set; }
    }
}
