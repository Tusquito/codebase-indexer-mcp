using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Embedding;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Services;

/// <summary>Port of Python recommend_code and find_outlier_chunks.</summary>
public sealed class RecommendService : IRecommendService
{
    private readonly IVectorStore _store;
    private readonly IDenseEmbedder _dense;
    private readonly DiscoveryOptions _discovery;

    /// <summary>Creates the recommend/outlier service.</summary>
    public RecommendService(
        IVectorStore store,
        [FromKeyedServices(EmbedderBackendKeys.Dense.Tei)] IDenseEmbedder dense,
        IOptions<DiscoveryOptions> discovery)
    {
        _store = store;
        _dense = dense;
        _discovery = discovery.Value;
    }

    /// <inheritdoc />
    public async Task<object> RecommendCodeAsync(
        string collection,
        IReadOnlyList<string>? positiveChunkIds = null,
        string? positiveQuery = null,
        IReadOnlyList<string>? negativeChunkIds = null,
        string? negativeQuery = null,
        int limit = 5,
        string? language = null,
        string? pathGlob = null,
        int? maxContentChars = null,
        CancellationToken cancellationToken = default)
    {
        if (limit > 20)
        {
            limit = 20;
        }

        var posIds = positiveChunkIds ?? Array.Empty<string>();
        var negIds = negativeChunkIds ?? Array.Empty<string>();
        var posQuery = (positiveQuery ?? "").Trim();
        var negQuery = (negativeQuery ?? "").Trim();

        if (posIds.Count == 0 && string.IsNullOrEmpty(posQuery))
        {
            throw new ArgumentException(
                "At least one positive example is required (positive_chunk_ids and/or positive_query).");
        }

        var exampleCount = posIds.Count + negIds.Count;
        if (!string.IsNullOrEmpty(posQuery))
        {
            exampleCount++;
        }

        if (!string.IsNullOrEmpty(negQuery))
        {
            exampleCount++;
        }

        if (exampleCount > _discovery.RecommendMaxExamples)
        {
            throw new ArgumentException(
                $"Example count {exampleCount} exceeds RECOMMEND_MAX_EXAMPLES=" +
                $"{_discovery.RecommendMaxExamples} (positive + negative combined).");
        }

        var allChunkIds = posIds.Concat(negIds).ToArray();
        if (allChunkIds.Length > 0)
        {
            await _store.VerifyChunkIdsExistAsync(collection, allChunkIds, cancellationToken)
                .ConfigureAwait(false);
        }

        var positive = posIds.Select(RecommendExample.FromChunkId).ToList();
        var negative = negIds.Select(RecommendExample.FromChunkId).ToList();

        var texts = new List<string>();
        var roles = new List<string>();
        if (!string.IsNullOrEmpty(posQuery))
        {
            texts.Add(posQuery);
            roles.Add("positive");
        }

        if (!string.IsNullOrEmpty(negQuery))
        {
            texts.Add(negQuery);
            roles.Add("negative");
        }

        if (texts.Count > 0)
        {
            var vectors = await _dense.EmbedBatchAsync(texts, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < roles.Count; i++)
            {
                if (roles[i] == "positive")
                {
                    positive.Add(RecommendExample.FromDenseVector(vectors[i]));
                }
                else
                {
                    negative.Add(RecommendExample.FromDenseVector(vectors[i]));
                }
            }
        }

        var results = await _store.RecommendAsync(
            collection, positive, negative.Count > 0 ? negative : null, limit, language, pathGlob, cancellationToken)
            .ConfigureAwait(false);

        return new RecommendCodeResponse(
            ShapeHits(results, maxContentChars),
            collection,
            positive.Count,
            negative.Count);
    }

    /// <inheritdoc />
    public async Task<object> FindOutlierChunksAsync(
        string collection,
        IReadOnlyList<string>? contextChunkIds = null,
        int limit = 5,
        string? language = null,
        string? pathGlob = null,
        float? maxSimilarity = null,
        int? maxContentChars = null,
        CancellationToken cancellationToken = default)
    {
        if (limit > 20)
        {
            limit = 20;
        }

        var ctxIds = contextChunkIds ?? Array.Empty<string>();
        if (ctxIds.Count > 0)
        {
            await _store.VerifyChunkIdsExistAsync(collection, ctxIds, cancellationToken)
                .ConfigureAwait(false);
        }

        if (maxSimilarity is { } ms && (ms < 0f || ms > 1f))
        {
            throw new ArgumentException("max_similarity must be between 0.0 and 1.0.");
        }

        var effectiveMaxSim = maxSimilarity ?? _discovery.OutlierMaxSimilarity;
        var results = await _store.FindOutlierChunksAsync(
            collection, ctxIds.Count > 0 ? ctxIds : null, limit, language, pathGlob, effectiveMaxSim,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var items = ShapeHits(results, maxContentChars, includeSimilarity: true);
        return new OutlierChunksResponse(items, collection, ctxIds.Count, effectiveMaxSim);
    }

    private static IReadOnlyList<DiscoveryHit> ShapeHits(
        IReadOnlyList<SearchHit> results,
        int? maxContentChars,
        bool includeSimilarity = false)
    {
        var items = new List<DiscoveryHit>(results.Count);
        foreach (var r in results)
        {
            var content = r.Content;
            bool? truncated = null;
            if (maxContentChars is { } max && content.Length > max)
            {
                content = content[..max];
                truncated = true;
            }

            items.Add(new DiscoveryHit(
                r.Id.Value,
                Math.Round(r.Score, 4),
                r.Collection,
                r.RelPath,
                r.SymbolName,
                r.SymbolType,
                r.StartLine,
                r.EndLine,
                r.Language,
                content,
                truncated,
                includeSimilarity ? Math.Round(r.Score, 4) : null));
        }

        return items;
    }
}
