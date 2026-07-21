using System.ComponentModel;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tools for hybrid codebase and symbol search.</summary>
[McpServerToolType]
public sealed class SearchTools
{
    private readonly ISearchService _search;
    private readonly QdrantOptions _qdrant;

    /// <summary>Creates search MCP tools.</summary>
    public SearchTools(ISearchService search, IOptions<QdrantOptions> qdrant)
    {
        _search = search;
        _qdrant = qdrant.Value;
    }

    /// <summary>Hybrid semantic + keyword search across indexed code.</summary>
    [McpServerTool(Name = "search_codebase"), Description(
        "This tool caps top_k at 20. When HYBRID_SEARCH is enabled (default), " +
        "results are ranked by reciprocal rank fusion — min_score is ignored. " +
        "When HYBRID_SEARCH is disabled, only dense cosine search runs and " +
        "min_score filters by similarity. See docs/SEARCH_BEHAVIOR.md. " +
        "Hybrid semantic + keyword search across indexed code. " +
        "Set max_content_chars to truncate chunk content; use get_chunk for full text. " +
        "When Embedding:RerankEnabled=true, ColBERT MAX_SIM reranks hybrid candidates; pass rerank=false to skip.")]
    public async Task<object> SearchCodebaseAsync(
        [Description("Natural-language or code search query")] string query,
        [Description("Max results (capped at 20)")] int top_k = 5,
        [Description("Primary collection (project folder name)")] string? collection = null,
        [Description("Additional collections to search")] string[]? collections = null,
        [Description("Optional language filter")] string? language = null,
        [Description("Min cosine score when hybrid is off")] float min_score = 0.5f,
        [Description("Truncate content to this many chars")] int? max_content_chars = null,
        [Description("ColBERT override: false skips rerank when enabled; true/null uses server default")] bool? rerank = null,
        CancellationToken cancellationToken = default) =>
        await _search.SearchCodebaseAsync(
            query,
            top_k,
            collection ?? _qdrant.Collection,
            collections,
            language,
            min_score,
            max_content_chars,
            rerank,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Token-efficient symbol lookup without code content.</summary>
    [McpServerTool(Name = "search_symbols"), Description(
        "This tool caps top_k at 30. Token-efficient symbol lookup: same hybrid search as " +
        "search_codebase but returns ONLY symbol metadata — no code content.")]
    public async Task<object> SearchSymbolsAsync(
        [Description("Natural-language or symbol search query")] string query,
        [Description("Max results (capped at 30)")] int top_k = 10,
        [Description("Primary collection")] string? collection = null,
        [Description("Additional collections")] string[]? collections = null,
        [Description("Optional language filter")] string? language = null,
        [Description("Min cosine score when hybrid is off")] float min_score = 0.4f,
        [Description("ColBERT override: false skips rerank when enabled; true/null uses server default")] bool? rerank = null,
        CancellationToken cancellationToken = default) =>
        await _search.SearchSymbolsAsync(
            query,
            top_k,
            collection ?? _qdrant.Collection,
            collections,
            language,
            min_score,
            rerank,
            cancellationToken).ConfigureAwait(false);
}
