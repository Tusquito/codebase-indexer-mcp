using System.ComponentModel;
using CodebaseIndexer.Application.Mapping;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Serialization;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tool: expand_search_context (gated by Graph:Enabled).</summary>
[McpServerToolType]
public sealed class ExpandSearchContextTools
{
    private readonly IExpandSearchContextService _service;

    /// <summary>Creates expand-search-context MCP tools.</summary>
    public ExpandSearchContextTools(IExpandSearchContextService service) => _service = service;

    /// <summary>Graph-augmented retrieval around hybrid search seeds.</summary>
    [McpServerTool(Name = "expand_search_context"), Description(
        "Graph-augmented retrieval (opt-in, only present when Graph:Enabled=true). " +
        "Runs the same hybrid search as search_codebase to find seed chunks, then " +
        "expands 1..Graph:MaxHops hops in the Neo4j code graph (CALLS, HTTP_CALLS, " +
        "DECLARES_ENDPOINT, DEFINES, IN_FILE, ...) capped by Graph:MaxNodes. Returns " +
        "a structured graph context (nodes, edges, related_chunks, seeds) — NOT an " +
        "answer. Use when a single search cannot surface structural neighbors " +
        "(callers/callees, endpoints, cross-file relationships). 'graph_hops' defaults " +
        "to Graph:MaxHops and is clamped to [1, Graph:MaxHops]. top_k is capped at 20. " +
        "Re-index after pull when enabling graph (no schema-version env). See docs/SEARCH_BEHAVIOR.md.")]
    public async Task<object> ExpandSearchContextAsync(
        [Description("Semantic search query for seed chunks")] string query,
        [Description("Max seed hits (capped at 20)")] int top_k = 5,
        [Description("Primary collection")] string? collection = null,
        [Description("Additional collections to search")] string[]? collections = null,
        [Description("Hop count (clamped to Graph:MaxHops)")] int? graph_hops = null,
        [Description("Optional language filter")] string? language = null,
        [Description("Minimum score for seed search")] float min_score = 0.5f,
        [Description("Truncate related chunk content to this many chars")] int? max_content_chars = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ExpandSearchContextAsync(
            query, top_k, collection, collections, graph_hops, ParseLanguage(language), min_score, max_content_chars, cancellationToken)
            .ConfigureAwait(false);
        return result.Match(v => v, McpErrorMapper.FromError);
    }

    private static SourceLanguage? ParseLanguage(string? language) =>
        DomainEnumWire.TryParse(language, out SourceLanguage value) ? value : null;
}
