using CodebaseIndexer.Application.BuildDeps;
using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using CodebaseIndexer.Domain.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace CodebaseIndexer.Application.Services;

/// <summary>Scroll/stats-backed read tools with short-lived memory cache.</summary>
public sealed class CollectionQueryService : ICollectionQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IVectorStore _store;
    private readonly IMemoryCache _cache;

    /// <summary>Creates the collection query service.</summary>
    public CollectionQueryService(IVectorStore store, IMemoryCache cache)
    {
        _store = store;
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<Result<object>> GetChunkAsync(
        string chunkId,
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        var payloadResult = await _store.FindChunkByIdAsync(chunkId, collection, cancellationToken)
            .ConfigureAwait(false);
        if (!payloadResult.IsSuccess)
        {
            return Result<object>.Failure(payloadResult.Error);
        }

        var payload = payloadResult.Value;
        return Result<object>.Success(new Dictionary<string, object?>
        {
            ["chunk_id"] = payload.ChunkId,
            ["rel_path"] = payload.RelPath,
            ["content"] = payload.Content,
            ["start_line"] = payload.StartLine,
            ["end_line"] = payload.EndLine,
            ["language"] = DomainEnumWire.ToWire(payload.Language),
            ["file_sha256"] = payload.FileSha256,
            ["symbol_name"] = payload.SymbolName ?? string.Empty,
            ["symbol_type"] = DomainEnumWire.ToWire(payload.SymbolType),
            ["file_mtime"] = 0.0,
        });
    }

    /// <inheritdoc />
    public async Task<Result<object>> GetFileOutlineAsync(
        string relPath,
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        var coll = collection ?? "codebase";
        var cacheKey = $"outline:{coll}:{relPath}";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached is not null)
        {
            return Result<object>.Success(cached);
        }

        var symbolsResult = await _store.ScrollFileSymbolsAsync(coll, relPath, cancellationToken)
            .ConfigureAwait(false);
        if (!symbolsResult.IsSuccess)
        {
            return Result<object>.Failure(symbolsResult.Error);
        }

        var symbols = symbolsResult.Value;
        if (symbols.Count == 0)
        {
            return Result<object>.Failure(new Error(
                ErrorKind.NotFound,
                StoreErrorCodes.ChunkNotFound,
                $"No symbols found for '{relPath}' in collection '{coll}'.",
                new Dictionary<string, string> { ["hint"] = "Check the path with search_symbols or list_collections." }));
        }

        var response = new FileOutlineResponse(
            coll,
            relPath,
            symbols.Count,
            symbols.Select(s => new FileOutlineSymbol(
                s.ChunkId,
                s.SymbolName,
                s.SymbolType,
                s.StartLine,
                s.EndLine,
                s.Language)).ToArray());

        _cache.Set(cacheKey, response, CacheTtl);
        return Result<object>.Success(response);
    }

    /// <inheritdoc />
    public async Task<Result<object>> GetCollectionSummaryAsync(
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        var coll = collection ?? "codebase";
        var cacheKey = $"summary:{coll}";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached is not null)
        {
            return Result<object>.Success(cached);
        }

        var rowsResult = await _store.ScrollAllPayloadsAsync(coll, cancellationToken).ConfigureAwait(false);
        if (!rowsResult.IsSuccess)
        {
            return Result<object>.Failure(rowsResult.Error);
        }

        var rows = rowsResult.Value;
        if (rows.Count == 0)
        {
            return Result<object>.Failure(new Error(
                ErrorKind.NotFound,
                StoreErrorCodes.CollectionNotFound,
                $"Collection '{coll}' is empty or does not exist.",
                new Dictionary<string, string> { ["hint"] = "Use index_codebase to index a project first." }));
        }

        var filesByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        var langCounter = new Dictionary<string, int>(StringComparer.Ordinal);
        var symbolTypeCounter = new Dictionary<string, int>(StringComparer.Ordinal);
        var chunksPerFile = new Dictionary<string, int>(StringComparer.Ordinal);
        var manifestPaths = new List<string>();

        foreach (var row in rows)
        {
            var languageWire = DomainEnumWire.ToWire(row.Language);
            var symbolTypeWire = DomainEnumWire.ToWire(row.SymbolType);

            if (!filesByPath.ContainsKey(row.RelPath))
            {
                filesByPath[row.RelPath] = languageWire;
                langCounter[languageWire] = langCounter.GetValueOrDefault(languageWire) + 1;
                if (BuildManifestDetector.IsBuildManifest(row.RelPath))
                {
                    manifestPaths.Add(row.RelPath);
                }
            }

            symbolTypeCounter[symbolTypeWire] = symbolTypeCounter.GetValueOrDefault(symbolTypeWire) + 1;
            chunksPerFile[row.RelPath] = chunksPerFile.GetValueOrDefault(row.RelPath) + 1;
        }

        IReadOnlyList<CollectionBuildDependency>? buildDependencies = null;
        if (manifestPaths.Count > 0)
        {
            var allStatsResult = await _store.ListCollectionStatsAsync(cancellationToken).ConfigureAwait(false);
            if (allStatsResult.IsSuccess)
            {
                var otherCollections = allStatsResult.Value.Where(s => s.Name != coll).Select(s => s.Name).ToArray();
                if (otherCollections.Length > 0)
                {
                    var manifestChunksResult = await _store.ScrollChunksByPathsAsync(
                        coll, manifestPaths, ["rel_path", "content"], cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    if (manifestChunksResult.IsSuccess)
                    {
                        var contentByPath = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (var chunk in manifestChunksResult.Value)
                        {
                            var path = chunk.GetValueOrDefault("rel_path", "");
                            if (string.IsNullOrEmpty(path))
                            {
                                continue;
                            }

                            contentByPath[path] = contentByPath.GetValueOrDefault(path, "") + "\n" + chunk.GetValueOrDefault("content", "");
                        }

                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        var deps = new List<CollectionBuildDependency>();
                        foreach (var (relPath, content) in contentByPath)
                        {
                            var extracted = BuildDepExtractor.Extract(content, relPath);
                            var matches = BuildDepCollectionMatcher.Match(extracted, otherCollections, coll);
                            foreach (var m in matches)
                            {
                                var key = $"{m.Artifact}:{m.MatchedCollection}";
                                if (!seen.Add(key))
                                {
                                    continue;
                                }

                                deps.Add(new CollectionBuildDependency(
                                    m.Artifact, m.Group, m.Version, m.Scope, m.Ecosystem,
                                    m.MatchedCollection, m.MatchConfidence, relPath));
                            }
                        }

                        if (deps.Count > 0)
                        {
                            buildDependencies = deps;
                        }
                    }
                }
            }
        }

        var response = new CollectionSummaryResponse(
            coll,
            filesByPath.Count,
            rows.Count,
            langCounter.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            symbolTypeCounter.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            TopLevelDirs(filesByPath.Keys.ToList(), depth: 2),
            chunksPerFile
                .OrderByDescending(kv => kv.Value)
                .Take(10)
                .Select(kv => new TopChunkedFile(kv.Key, kv.Value))
                .ToArray(),
            buildDependencies);

        _cache.Set(cacheKey, response, CacheTtl);
        return Result<object>.Success(response);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CollectionListItem>>> ListCollectionsAsync(
        CancellationToken cancellationToken = default)
    {
        var statsResult = await _store.ListCollectionStatsAsync(cancellationToken).ConfigureAwait(false);
        if (!statsResult.IsSuccess)
        {
            return Result<IReadOnlyList<CollectionListItem>>.Failure(statsResult.Error);
        }

        return Result<IReadOnlyList<CollectionListItem>>.Success(statsResult.Value.Select(s => new CollectionListItem(
            s.Name,
            s.VectorCount,
            s.DiskSizeMb,
            s.DenseEmbedModel,
            s.SparseEmbedModel,
            s.Hybrid,
            s.RerankEnabled,
            string.IsNullOrEmpty(s.ColbertEmbedModel) ? null : s.ColbertEmbedModel)).ToArray());
    }

    internal static IReadOnlyList<string> TopLevelDirs(IReadOnlyList<string> relPaths, int depth = 2)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in relPaths)
        {
            var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var dirParts = parts.Length > 0 ? parts[..^1] : [];
            for (var d = 1; d <= Math.Min(depth, dirParts.Length); d++)
            {
                var prefix = string.Join('/', dirParts.Take(d));
                if (!string.IsNullOrEmpty(prefix))
                {
                    seen.Add(prefix);
                }
            }
        }

        return seen.OrderBy(s => s, StringComparer.Ordinal).ToArray();
    }
}
