using System.Collections.Concurrent;
using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Domain.Embedding;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Services;

/// <summary>Embeds queries and dispatches hybrid/dense search with cross-collection RRF.</summary>
public sealed class SearchService : ISearchService
{
    private static readonly ConcurrentDictionary<string, byte> WarnedUnlinkedCollections = new(StringComparer.Ordinal);

    private readonly IVectorStore _store;
    private readonly IDenseEmbedder _dense;
    private readonly ISparseEmbedder _sparse;
    private readonly EmbeddingOptions _embedding;
    private readonly GraphOptions _graph;
    private readonly ILogger<SearchService> _logger;

    /// <summary>Creates the search service.</summary>
    public SearchService(
        IVectorStore store,
        [FromKeyedServices(EmbedderBackendKeys.Dense.Tei)] IDenseEmbedder dense,
        [FromKeyedServices(EmbedderBackendKeys.Sparse.Onnx)] ISparseEmbedder sparse,
        IOptions<EmbeddingOptions> embedding,
        IOptions<GraphOptions> graph,
        ILogger<SearchService> logger)
    {
        _store = store;
        _dense = dense;
        _sparse = sparse;
        _embedding = embedding.Value;
        _graph = graph.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SearchCodebaseResponse> SearchCodebaseAsync(
        string query,
        int topK = 5,
        string? collection = null,
        IReadOnlyList<string>? collections = null,
        string? language = null,
        float minScore = 0.5f,
        int? maxContentChars = null,
        bool? rerank = null,
        CancellationToken cancellationToken = default)
    {
        _ = rerank; // Phase 6 ColBERT — accepted and ignored while RerankEnabled=false
        if (topK > 20)
        {
            topK = 20;
        }

        var targets = ResolveCollections(collection ?? "codebase", collections);
        await WarnIfGraphLinkageMissingAsync(targets, cancellationToken).ConfigureAwait(false);
        var hits = await RunSearchAsync(query, targets, topK, language, minScore, cancellationToken)
            .ConfigureAwait(false);

        var items = new List<SearchCodebaseHit>(hits.Count);
        foreach (var hit in hits)
        {
            var content = hit.Content;
            bool? truncated = null;
            if (maxContentChars is { } max && content.Length > max)
            {
                content = content[..max];
                truncated = true;
            }

            items.Add(new SearchCodebaseHit(
                hit.Id.Value,
                Math.Round(hit.Score, 4),
                hit.Collection,
                hit.RelPath,
                hit.SymbolName,
                hit.SymbolType,
                hit.StartLine,
                hit.EndLine,
                hit.Language,
                content,
                truncated));
        }

        var crossRefs = targets.Count > 1
            ? await DetectCrossReferencesAsync(hits, targets, cancellationToken).ConfigureAwait(false)
            : Array.Empty<CrossReferenceEntry>();

        return new SearchCodebaseResponse(items, targets, crossRefs);
    }

    /// <inheritdoc />
    public async Task<SearchSymbolsResponse> SearchSymbolsAsync(
        string query,
        int topK = 10,
        string? collection = null,
        IReadOnlyList<string>? collections = null,
        string? language = null,
        float minScore = 0.4f,
        bool? rerank = null,
        CancellationToken cancellationToken = default)
    {
        _ = rerank;
        if (topK > 30)
        {
            topK = 30;
        }

        var targets = ResolveCollections(collection ?? "codebase", collections);
        await WarnIfGraphLinkageMissingAsync(targets, cancellationToken).ConfigureAwait(false);
        var hits = await RunSearchAsync(query, targets, topK, language, minScore, cancellationToken)
            .ConfigureAwait(false);

        var items = hits.Select(hit => new SearchSymbolsHit(
            hit.Id.Value,
            Math.Round(hit.Score, 4),
            hit.Collection,
            hit.RelPath,
            hit.SymbolName,
            hit.SymbolType,
            hit.StartLine,
            hit.EndLine,
            hit.Language)).ToArray();

        return new SearchSymbolsResponse(items, targets);
    }

    internal static IReadOnlyList<string> ResolveCollections(string primary, IReadOnlyList<string>? collections)
    {
        var target = new List<string> { primary };
        if (collections is not null)
        {
            foreach (var c in collections)
            {
                if (!target.Contains(c, StringComparer.Ordinal))
                {
                    target.Add(c);
                }
            }
        }

        return target;
    }

    private async Task WarnIfGraphLinkageMissingAsync(
        IReadOnlyList<string> collections,
        CancellationToken cancellationToken)
    {
        if (!_graph.Enabled)
        {
            return;
        }

        foreach (var coll in collections)
        {
            if (!WarnedUnlinkedCollections.TryAdd(coll, 0))
            {
                continue;
            }

            try
            {
                if (!await _store.CollectionHasGraphEnabledAsync(coll, cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogWarning("graph_linkage_missing collection={Collection}", coll);
                }
            }
            catch (Exception)
            {
                WarnedUnlinkedCollections.TryRemove(coll, out _);
            }
        }
    }

    private async Task<IReadOnlyList<SearchHit>> RunSearchAsync(
        string query,
        IReadOnlyList<string> targets,
        int topK,
        string? language,
        float minScore,
        CancellationToken cancellationToken)
    {
        var denseTask = _dense.EmbedQueryAsync([query], cancellationToken);
        Task<IReadOnlyList<SparseVector>>? sparseTask = null;
        if (_embedding.HybridSearch)
        {
            sparseTask = _sparse.EmbedBatchAsync([query], cancellationToken);
        }

        var denseVectors = await denseTask.ConfigureAwait(false);
        SparseVector? sparse = null;
        if (sparseTask is not null)
        {
            var sparseVectors = await sparseTask.ConfigureAwait(false);
            sparse = sparseVectors[0];
        }

        var dense = denseVectors[0];

        if (targets.Count == 1)
        {
            return await _store.SearchAsync(
                targets[0], dense, sparse, topK, language, minScore, cancellationToken).ConfigureAwait(false);
        }

        var tasks = targets.Select(coll =>
            _store.SearchAsync(coll, dense, sparse, topK, language, minScore, cancellationToken));
        var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
        var perCollection = batches
            .Where(b => b.Count > 0)
            .Cast<IReadOnlyList<SearchHit>>()
            .ToArray();

        if (perCollection.Length == 0)
        {
            return Array.Empty<SearchHit>();
        }

        if (perCollection.Length == 1)
        {
            return perCollection[0].Take(topK).ToArray();
        }

        return CrossCollectionRrf.Fuse(perCollection, _embedding.RrfK, topK);
    }

    private async Task<IReadOnlyList<CrossReferenceEntry>> DetectCrossReferencesAsync(
        IReadOnlyList<SearchHit> results,
        IReadOnlyList<string> targetCollections,
        CancellationToken cancellationToken)
    {
        var symbolCollections = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var r in results)
        {
            if (string.IsNullOrEmpty(r.SymbolName))
            {
                continue;
            }

            if (!symbolCollections.TryGetValue(r.SymbolName, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                symbolCollections[r.SymbolName] = set;
            }

            set.Add(r.Collection);
        }

        var toCheck = new List<(string Symbol, List<string> Missing)>();
        foreach (var (sym, colls) in symbolCollections)
        {
            var missing = targetCollections.Where(c => !colls.Contains(c)).ToList();
            if (missing.Count > 0)
            {
                toCheck.Add((sym, missing));
            }
        }

        if (toCheck.Count > 0)
        {
            var foundBatches = await Task.WhenAll(
                toCheck.Select(t =>
                    _store.FindSymbolInCollectionsAsync(t.Symbol, t.Missing, limitPerCollection: 3, cancellationToken)))
                .ConfigureAwait(false);

            for (var i = 0; i < toCheck.Count; i++)
            {
                foreach (var hit in foundBatches[i])
                {
                    symbolCollections[toCheck[i].Symbol].Add(hit.Collection);
                }
            }
        }

        var crossRefs = new List<CrossReferenceEntry>();
        foreach (var (sym, colls) in symbolCollections)
        {
            if (colls.Count < 2)
            {
                continue;
            }

            var locations = new Dictionary<string, List<CrossReferenceLocation>>(StringComparer.Ordinal);
            foreach (var r in results)
            {
                if (r.SymbolName != sym)
                {
                    continue;
                }

                var entry = new CrossReferenceLocation(
                    $"{r.RelPath}:{r.StartLine}",
                    ReferenceClassifier.Classify(r.Content, sym));

                if (!locations.TryGetValue(r.Collection, out var list))
                {
                    list = [];
                    locations[r.Collection] = list;
                }

                if (!list.Any(e => e.Path == entry.Path && e.ReferenceType == entry.ReferenceType))
                {
                    list.Add(entry);
                }
            }

            crossRefs.Add(new CrossReferenceEntry(
                sym,
                colls.OrderBy(c => c, StringComparer.Ordinal).ToArray(),
                locations.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<CrossReferenceLocation>)kv.Value,
                    StringComparer.Ordinal)));
        }

        return crossRefs
            .OrderByDescending(x => x.Collections.Count)
            .ToArray();
    }
}
