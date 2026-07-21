using System.ComponentModel;
using CodebaseIndexer.Application.Services;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tool: find_cross_references.</summary>
[McpServerToolType]
public sealed class CrossReferenceTools
{
    private readonly ICrossReferenceService _service;

    /// <summary>Creates cross-reference MCP tools.</summary>
    public CrossReferenceTools(ICrossReferenceService service) => _service = service;

    /// <summary>Find cross-project links for a symbol, endpoint, concept, or member call site.</summary>
    [McpServerTool(Name = "find_cross_references"), Description(
        "Find cross-project links for a symbol, endpoint, concept, or any query " +
        "across multiple indexed collections. Discovers: direct code links " +
        "(imports, class usage, call sites), HTTP endpoint connections, shared " +
        "DTOs/error codes, and semantic relationships. " +
        "Each result is classified as: definition, import, usage, " +
        "endpoint_definition, http_call, service_config, build_dependency, or call_site. " +
        "Use 'query' for semantic search or 'symbol_name' for exact symbol matching. " +
        "For precise method call-site retrieval, pass 'member' and optionally 'receiver' " +
        "(indexed callees Path D via Qdrant; re-index after pull to populate callees). " +
        "When RERANK_ENABLED=true, pass rerank=false to skip ColBERT (Phase 6; currently no-op).")]
    public Task<object> FindCrossReferencesAsync(
        [Description("Semantic search query")] string? query = null,
        [Description("Exact symbol name")] string? symbol_name = null,
        [Description("Collections to search; omit for all")] string[]? collections = null,
        [Description("Max results per path")] int top_k = 10,
        [Description("Method name for Path D call-site lookup")] string? member = null,
        [Description("Optional receiver/field name for Path D")] string? receiver = null,
        [Description("ColBERT rerank override (ignored until Phase 6)")] bool? rerank = null,
        CancellationToken cancellationToken = default) =>
        _service.FindCrossReferencesAsync(
            query, symbol_name, collections, top_k, member, receiver, rerank, cancellationToken);
}
