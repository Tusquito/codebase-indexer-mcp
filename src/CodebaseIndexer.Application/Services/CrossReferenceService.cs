using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.Logging;

namespace CodebaseIndexer.Application.Services;

/// <summary>Port of Python find_cross_references (Path D: Neo4j when graph-ready, else Qdrant).</summary>
public sealed class CrossReferenceService : ICrossReferenceService
{
    private readonly ISearchService _search;
    private readonly IVectorStore _store;
    private readonly IGraphStore _graph;
    private readonly UrlExtractors _extractors;
    private readonly ILogger<CrossReferenceService> _logger;

    /// <summary>Creates the cross-reference service.</summary>
    public CrossReferenceService(
        ISearchService search,
        IVectorStore store,
        IGraphStore graph,
        UrlExtractors extractors,
        ILogger<CrossReferenceService> logger)
    {
        _search = search;
        _store = store;
        _graph = graph;
        _extractors = extractors;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<object> FindCrossReferencesAsync(
        string? query = null,
        string? symbolName = null,
        IReadOnlyList<string>? collections = null,
        int topK = 10,
        string? member = null,
        string? receiver = null,
        bool? rerank = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)
            && string.IsNullOrWhiteSpace(symbolName)
            && string.IsNullOrWhiteSpace(member))
        {
            return new Dictionary<string, object?>
            {
                ["error"] = "Provide at least 'query', 'symbol_name', or 'member'.",
            };
        }

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

        if (targetCollections.Count == 0)
        {
            return new CrossReferenceResponse(query, symbolName, member, receiver, 0, new Dictionary<string, IReadOnlyList<CrossReferenceHit>>(), Array.Empty<CrossReferenceLink>());
        }

        var allResults = new List<CrossReferenceHitInternal>();
        var lookupLabel = query ?? symbolName ?? "";

        if (!string.IsNullOrWhiteSpace(query))
        {
            var semantic = await _search.SearchCodebaseAsync(
                query!,
                topK,
                targetCollections[0],
                targetCollections.Count > 1 ? targetCollections.Skip(1).ToArray() : null,
                language: null,
                minScore: 0.3f,
                rerank: rerank,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var r in semantic.Results)
            {
                allResults.Add(ToHit(r, "semantic", _extractors.ClassifyReference(r.Content, lookupLabel, r.RelPath)));
            }
        }

        if (!string.IsNullOrWhiteSpace(symbolName))
        {
            var symbolResults = await _store.FindSymbolInCollectionsAsync(
                symbolName!, targetCollections, topK, cancellationToken).ConfigureAwait(false);
            var seen = allResults.Select(r => r.RelPath + r.StartLine).ToHashSet(StringComparer.Ordinal);
            foreach (var r in symbolResults)
            {
                var key = r.RelPath + r.StartLine;
                if (!seen.Add(key))
                {
                    continue;
                }

                allResults.Add(new CrossReferenceHitInternal(
                    r.RelPath, r.SymbolName, r.SymbolType, r.StartLine, r.EndLine, r.Language,
                    r.Content, 1.0, r.Collection, "exact_symbol",
                    _extractors.ClassifyReference(r.Content, symbolName!, r.RelPath)));
            }

            var importQuery = $"import {symbolName}";
            var importResults = await _search.SearchCodebaseAsync(
                importQuery,
                topK,
                targetCollections[0],
                targetCollections.Count > 1 ? targetCollections.Skip(1).ToArray() : null,
                language: null,
                minScore: 0.3f,
                rerank: rerank,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            seen = allResults.Select(r => r.RelPath + r.StartLine).ToHashSet(StringComparer.Ordinal);
            foreach (var r in importResults.Results)
            {
                var key = r.RelPath + r.StartLine;
                if (!seen.Add(key))
                {
                    continue;
                }

                allResults.Add(ToHit(r, "import_search", _extractors.ClassifyReference(r.Content, symbolName!, r.RelPath)));
            }
        }

        if (!string.IsNullOrWhiteSpace(member))
        {
            var callerResults = await FindCallSitesAsync(
                member!, targetCollections, receiver, topK, cancellationToken).ConfigureAwait(false);
            var byKey = allResults.ToDictionary(r => r.RelPath + r.StartLine, StringComparer.Ordinal);
            foreach (var r in callerResults)
            {
                var key = r.RelPath + r.StartLine;
                if (byKey.TryGetValue(key, out var existing))
                {
                    existing.MatchType = "call_site";
                    existing.ReferenceType = "call_site";
                    existing.Score = 1.0;
                }
                else
                {
                    var entry = new CrossReferenceHitInternal(
                        r.RelPath, r.SymbolName, r.SymbolType, r.StartLine, r.EndLine, r.Language,
                        r.Content, 1.0, r.Collection, "call_site", "call_site");
                    allResults.Add(entry);
                    byKey[key] = entry;
                }
            }
        }

        var byCollection = allResults
            .GroupBy(r => r.Collection, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CrossReferenceHit>)g.Select(h => h.ToPublic()).ToArray(),
                StringComparer.Ordinal);

        var links = BuildLinkSummary(byCollection, _extractors, member, symbolName);
        return new CrossReferenceResponse(
            query, symbolName, member, receiver, byCollection.Count, byCollection, links);
    }

    private async Task<IReadOnlyList<SearchHit>> FindCallSitesAsync(
        string member,
        IReadOnlyList<string> targetCollections,
        string? receiver,
        int topK,
        CancellationToken cancellationToken)
    {
        var neo4jCollections = new List<string>();
        var qdrantCollections = new List<string>(targetCollections);

        if (await _graph.IsEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            neo4jCollections.Clear();
            qdrantCollections.Clear();
            foreach (var coll in targetCollections)
            {
                if (await _store.CollectionHasGraphCallSitesAsync(coll, cancellationToken).ConfigureAwait(false))
                {
                    neo4jCollections.Add(coll);
                }
                else
                {
                    qdrantCollections.Add(coll);
                    _logger.LogWarning(
                        "call_site_qdrant_fallback collection={Collection} hint={Hint}",
                        coll,
                        "Collection lacks graph_call_sites metadata; re-index with Graph:Enabled=true");
                }
            }
        }

        var callerResults = new List<SearchHit>();
        if (neo4jCollections.Count > 0)
        {
            callerResults.AddRange(
                await _graph.FindCallersAsync(member, neo4jCollections, receiver, topK, cancellationToken)
                    .ConfigureAwait(false));
        }

        if (qdrantCollections.Count > 0)
        {
            callerResults.AddRange(
                await _store.FindCallersInCollectionsAsync(
                    member, qdrantCollections, receiver, topK, cancellationToken).ConfigureAwait(false));
        }

        return callerResults;
    }

    private static CrossReferenceHitInternal ToHit(SearchCodebaseHit r, string matchType, string referenceType) =>
        new(
            r.RelPath, r.SymbolName, r.SymbolType, r.StartLine, r.EndLine, r.Language,
            r.Content, r.Score, r.Collection, matchType, referenceType);

    private static IReadOnlyList<CrossReferenceLink> BuildLinkSummary(
        IReadOnlyDictionary<string, IReadOnlyList<CrossReferenceHit>> byCollection,
        UrlExtractors extractors,
        string? member,
        string? symbolName)
    {
        var links = new List<CrossReferenceLink>();
        var endpoints = new List<(string Coll, CrossReferenceHit Hit, IReadOnlyList<string> Paths)>();
        var callers = new List<(string Coll, CrossReferenceHit Hit, IReadOnlyList<string> Paths)>();
        var definitions = new List<(string Coll, CrossReferenceHit Hit)>();
        var usages = new List<(string Coll, CrossReferenceHit Hit)>();
        var callSites = new List<(string Coll, CrossReferenceHit Hit)>();

        foreach (var (coll, results) in byCollection)
        {
            foreach (var r in results)
            {
                switch (r.ReferenceType)
                {
                    case "endpoint_definition":
                        endpoints.Add((coll, r, UrlExtractors.RoutePaths(r.Content, r.RelPath)));
                        break;
                    case "http_call":
                        callers.Add((coll, r, extractors.CodeUrls(r.Content)));
                        break;
                    case "service_config":
                        callers.Add((coll, r, extractors.ConfigUrls(r.Content).Paths));
                        break;
                    case "definition":
                        definitions.Add((coll, r));
                        break;
                    case "call_site":
                        callSites.Add((coll, r));
                        break;
                    case "usage":
                    case "import":
                        usages.Add((coll, r));
                        break;
                }
            }
        }

        var seenLinks = new HashSet<(string, string, string, string)>();
        foreach (var (defColl, defR) in definitions)
        {
            var defSymbol = defR.SymbolName ?? "";
            foreach (var (useColl, useR) in callSites)
            {
                if (!string.IsNullOrEmpty(member))
                {
                    if (!string.IsNullOrEmpty(symbolName) && !string.IsNullOrEmpty(defSymbol) && defSymbol != symbolName)
                    {
                        continue;
                    }
                }
                else if (!string.IsNullOrEmpty(symbolName) && !string.IsNullOrEmpty(defSymbol) && defSymbol != symbolName)
                {
                    continue;
                }

                var linkKey = (useColl, useR.RelPath, defColl, defR.RelPath);
                if (!seenLinks.Add(linkKey))
                {
                    continue;
                }

                links.Add(new CrossReferenceLink(
                    "code_dependency",
                    new CrossReferenceLinkEnd(useColl, useR.RelPath, "call_site"),
                    new CrossReferenceLinkEnd(defColl, defR.RelPath, null, defSymbol)));
            }
        }

        if (byCollection.Count < 2)
        {
            return links;
        }

        foreach (var (callColl, callR, callPaths) in callers)
        {
            foreach (var (epColl, epR, epPaths) in endpoints)
            {
                if (callColl == epColl)
                {
                    continue;
                }

                var matched = new List<string>();
                foreach (var cp in callPaths)
                {
                    foreach (var ep in epPaths)
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

                var linkKey = (callColl, callR.RelPath, epColl, epR.RelPath);
                if (!seenLinks.Add(linkKey))
                {
                    continue;
                }

                links.Add(new CrossReferenceLink(
                    "http_dependency",
                    new CrossReferenceLinkEnd(callColl, callR.RelPath, callR.ReferenceType),
                    new CrossReferenceLinkEnd(epColl, epR.RelPath, "endpoint_definition"),
                    matched));
            }
        }

        foreach (var (defColl, defR) in definitions)
        {
            var defSymbol = defR.SymbolName ?? "";
            foreach (var (useColl, useR) in usages)
            {
                if (defColl == useColl)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(defSymbol) && !useR.Content.Contains(defSymbol, StringComparison.Ordinal))
                {
                    continue;
                }

                var linkKey = (useColl, useR.RelPath, defColl, defR.RelPath);
                if (!seenLinks.Add(linkKey))
                {
                    continue;
                }

                links.Add(new CrossReferenceLink(
                    "code_dependency",
                    new CrossReferenceLinkEnd(useColl, useR.RelPath, useR.ReferenceType),
                    new CrossReferenceLinkEnd(defColl, defR.RelPath, null, defSymbol)));
            }
        }

        return links;
    }

    private sealed class CrossReferenceHitInternal
    {
        public CrossReferenceHitInternal(
            string relPath, string? symbolName, SymbolType symbolType, int startLine, int endLine,
            SourceLanguage language, string content, double score, string collection, string matchType, string referenceType)
        {
            RelPath = relPath;
            SymbolName = symbolName;
            SymbolType = symbolType;
            StartLine = startLine;
            EndLine = endLine;
            Language = language;
            Content = content;
            Score = score;
            Collection = collection;
            MatchType = matchType;
            ReferenceType = referenceType;
        }

        public string RelPath { get; }
        public string? SymbolName { get; }
        public SymbolType SymbolType { get; }
        public int StartLine { get; }
        public int EndLine { get; }
        public SourceLanguage Language { get; }
        public string Content { get; }
        public double Score { get; set; }
        public string Collection { get; }
        public string MatchType { get; set; }
        public string ReferenceType { get; set; }

        public CrossReferenceHit ToPublic() => new(
            RelPath, SymbolName, SymbolType, StartLine, EndLine, Language, Content,
            Math.Round(Score, 4), MatchType, ReferenceType);
    }
}
