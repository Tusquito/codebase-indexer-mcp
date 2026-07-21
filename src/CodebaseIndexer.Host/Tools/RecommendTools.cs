using System.ComponentModel;
using CodebaseIndexer.Application.Services;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tool: recommend_code.</summary>
[McpServerToolType]
public sealed class RecommendTools
{
    private readonly IRecommendService _service;

    /// <summary>Creates recommend MCP tools.</summary>
    public RecommendTools(IRecommendService service) => _service = service;

    /// <summary>Recommend chunks similar to positives / dissimilar from negatives.</summary>
    [McpServerTool(Name = "recommend_code"), Description(
        "Find code chunks similar to positive examples and dissimilar from " +
        "negative examples using Qdrant's Recommendation API on the dense " +
        "vector only (AVERAGE_VECTOR strategy). " +
        "Provide positive examples via positive_chunk_ids and/or positive_query; " +
        "optional negatives via negative_chunk_ids and/or negative_query. " +
        "Requires a single collection. Missing chunk IDs fail fast. " +
        "path_glob is applied as a post-filter. Example count capped by Discovery:RecommendMaxExamples. " +
        "limit capped at 20. Gated by Discovery:RecommendEnabled. See docs/SEARCH_BEHAVIOR.md.")]
    public Task<object> RecommendCodeAsync(
        [Description("Collection name")] string collection,
        [Description("Positive example chunk ids")] string[]? positive_chunk_ids = null,
        [Description("Positive free-text example")] string? positive_query = null,
        [Description("Negative example chunk ids")] string[]? negative_chunk_ids = null,
        [Description("Negative free-text example")] string? negative_query = null,
        [Description("Max results (capped at 20)")] int limit = 5,
        [Description("Optional language filter")] string? language = null,
        [Description("Optional fnmatch path filter")] string? path_glob = null,
        [Description("Truncate content to this many chars")] int? max_content_chars = null,
        CancellationToken cancellationToken = default) =>
        _service.RecommendCodeAsync(
            collection, positive_chunk_ids, positive_query, negative_chunk_ids, negative_query,
            limit, language, path_glob, max_content_chars, cancellationToken);
}
