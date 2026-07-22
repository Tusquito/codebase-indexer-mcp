using System.ComponentModel;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Serialization;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tool: find_outlier_chunks.</summary>
[McpServerToolType]
public sealed class OutlierTools
{
    private readonly IRecommendService _service;

    /// <summary>Creates outlier MCP tools.</summary>
    public OutlierTools(IRecommendService service) => _service = service;

    /// <summary>Find chunks semantically distant from a context sample.</summary>
    [McpServerTool(Name = "find_outlier_chunks"), Description(
        "Find code chunks semantically distant from a defined context " +
        "(module path scope and/or explicit reference chunk IDs). " +
        "Uses Qdrant Recommendation API with BEST_SCORE negative-only " +
        "examples on the dense vector, then scores by cosine similarity " +
        "to the context centroid (lower = more distant). " +
        "Requires a single collection. Context bounded by Discovery:OutlierMaxContextSamples. " +
        "Chunks above max_similarity are excluded. limit capped at 20. " +
        "Gated by Discovery:RecommendEnabled. See docs/SEARCH_BEHAVIOR.md.")]
    public Task<object> FindOutlierChunksAsync(
        [Description("Collection name")] string collection,
        [Description("Context chunk ids")] string[]? context_chunk_ids = null,
        [Description("Max results (capped at 20)")] int limit = 5,
        [Description("Optional language filter")] string? language = null,
        [Description("Optional fnmatch path filter for context/candidates")] string? path_glob = null,
        [Description("Exclude candidates above this cosine similarity")] float? max_similarity = null,
        [Description("Truncate content to this many chars")] int? max_content_chars = null,
        CancellationToken cancellationToken = default) =>
        _service.FindOutlierChunksAsync(
            collection, context_chunk_ids, limit, ParseLanguage(language), path_glob, max_similarity,
            max_content_chars, cancellationToken);

    private static SourceLanguage? ParseLanguage(string? language) =>
        DomainEnumWire.TryParse(language, out SourceLanguage value) ? value : null;
}
