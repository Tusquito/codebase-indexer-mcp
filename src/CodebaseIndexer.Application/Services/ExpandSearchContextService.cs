using System.Text.Json.Serialization;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Services;

/// <summary>Port of Python <c>tools/graph_search.py</c> expand_search_context.</summary>
public sealed class ExpandSearchContextService : IExpandSearchContextService
{
    private readonly ISearchService _search;
    private readonly IVectorStore _store;
    private readonly IGraphStore _graph;
    private readonly GraphOptions _graphOptions;

    /// <summary>Creates the expand-search-context service.</summary>
    public ExpandSearchContextService(
        ISearchService search,
        IVectorStore store,
        IGraphStore graph,
        IOptions<GraphOptions> graphOptions)
    {
        _search = search;
        _store = store;
        _graph = graph;
        _graphOptions = graphOptions.Value;
    }

    /// <inheritdoc />
    public async Task<Result<object>> ExpandSearchContextAsync(
        string query,
        int topK = 5,
        string? collection = null,
        IReadOnlyList<string>? collections = null,
        int? graphHops = null,
        SourceLanguage? language = null,
        float minScore = 0.5f,
        int? maxContentChars = null,
        CancellationToken cancellationToken = default)
    {
        if (topK > 20)
        {
            topK = 20;
        }

        var primary = collection ?? "codebase";
        var targets = SearchService.ResolveCollections(primary, collections);
        var searchResult = await _search.SearchCodebaseAsync(
            query, topK, primary, collections, language, minScore, maxContentChars: null, rerank: null, cancellationToken)
            .ConfigureAwait(false);
        if (!searchResult.IsSuccess)
        {
            return Result<object>.Failure(searchResult.Error);
        }

        var search = searchResult.Value;
        var seeds = search.Results.Select(r => new ExpandSeed(
            r.ChunkId, r.Score, r.Collection, r.RelPath, r.SymbolName, r.SymbolType,
            r.StartLine, r.EndLine, r.Language)).ToArray();

        if (!await _graph.IsEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return Result<object>.Success(EmptyContext(seeds));
        }

        var seedChunkIds = search.Results
            .Select(r => r.ChunkId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToArray();
        if (seedChunkIds.Length == 0)
        {
            return Result<object>.Success(EmptyContext(seeds));
        }

        var hops = graphHops ?? _graphOptions.MaxHops;
        hops = Math.Max(1, Math.Min(hops, _graphOptions.MaxHops));

        var expansionResult = await _graph.ExpandSubgraphAsync(
            seedChunkIds, hops, _graphOptions.MaxNodes, cancellationToken).ConfigureAwait(false);
        if (!expansionResult.IsSuccess)
        {
            return Result<object>.Failure(expansionResult.Error);
        }

        var expansion = expansionResult.Value;
        var relatedChunks = new List<ExpandRelatedChunk>();
        foreach (var cid in expansion.RelatedChunkIds)
        {
            expansion.RelatedChunkCollections.TryGetValue(cid, out var coll);
            var payloadResult = coll is not null
                ? await _store.GetChunkByIdAsync(coll, cid, cancellationToken).ConfigureAwait(false)
                : await _store.FindChunkByIdAsync(cid, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!payloadResult.IsSuccess)
            {
                if (payloadResult.Error.Kind == ErrorKind.NotFound)
                {
                    continue;
                }

                return Result<object>.Failure(payloadResult.Error);
            }

            var payload = payloadResult.Value;
            var content = payload.Content ?? string.Empty;
            bool? truncated = null;
            if (maxContentChars is { } max && content.Length > max)
            {
                content = content[..max];
                truncated = true;
            }

            relatedChunks.Add(new ExpandRelatedChunk(
                cid,
                coll ?? payload.Collection,
                payload.RelPath,
                payload.SymbolName,
                payload.SymbolType,
                payload.StartLine,
                payload.EndLine,
                payload.Language,
                content,
                truncated));
        }

        return Result<object>.Success(new ExpandSearchContextResponse(
            expansion.Nodes.Select(n => new ExpandNode(n.Labels, n.Key, n.Props)).ToArray(),
            expansion.Edges.Select(e => new ExpandEdge(e.Type, e.FromKey, e.ToKey)).ToArray(),
            relatedChunks,
            seeds,
            targets,
            hops));
    }

    private static ExpandSearchContextResponse EmptyContext(IReadOnlyList<ExpandSeed> seeds) =>
        new(Array.Empty<ExpandNode>(), Array.Empty<ExpandEdge>(), Array.Empty<ExpandRelatedChunk>(), seeds, null, null);

    private sealed record ExpandSearchContextResponse(
        [property: JsonPropertyName("nodes")] IReadOnlyList<ExpandNode> Nodes,
        [property: JsonPropertyName("edges")] IReadOnlyList<ExpandEdge> Edges,
        [property: JsonPropertyName("related_chunks")] IReadOnlyList<ExpandRelatedChunk> RelatedChunks,
        [property: JsonPropertyName("seeds")] IReadOnlyList<ExpandSeed> Seeds,
        [property: JsonPropertyName("collections_searched")] IReadOnlyList<string>? CollectionsSearched,
        [property: JsonPropertyName("graph_hops")] int? GraphHops);

    private sealed record ExpandNode(
        [property: JsonPropertyName("labels")] IReadOnlyList<string> Labels,
        [property: JsonPropertyName("key")] string? Key,
        [property: JsonPropertyName("props")] IReadOnlyDictionary<string, object?> Props);

    private sealed record ExpandEdge(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("from")] string? From,
        [property: JsonPropertyName("to")] string? To);

    private sealed record ExpandSeed(
        [property: JsonPropertyName("chunk_id")] string ChunkId,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("collection")] string Collection,
        [property: JsonPropertyName("rel_path")] string RelPath,
        [property: JsonPropertyName("symbol_name")] string? SymbolName,
        [property: JsonPropertyName("symbol_type")] SymbolType SymbolType,
        [property: JsonPropertyName("start_line")] int StartLine,
        [property: JsonPropertyName("end_line")] int EndLine,
        [property: JsonPropertyName("language")] SourceLanguage Language);

    private sealed record ExpandRelatedChunk(
        [property: JsonPropertyName("chunk_id")] string ChunkId,
        [property: JsonPropertyName("collection")] string? Collection,
        [property: JsonPropertyName("rel_path")] string RelPath,
        [property: JsonPropertyName("symbol_name")] string? SymbolName,
        [property: JsonPropertyName("symbol_type")] SymbolType SymbolType,
        [property: JsonPropertyName("start_line")] int StartLine,
        [property: JsonPropertyName("end_line")] int EndLine,
        [property: JsonPropertyName("language")] SourceLanguage Language,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("content_truncated")] bool? ContentTruncated);
}
