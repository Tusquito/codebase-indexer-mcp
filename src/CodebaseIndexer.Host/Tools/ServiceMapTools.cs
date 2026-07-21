using System.ComponentModel;
using CodebaseIndexer.Application.Services;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tool: map_service_dependencies.</summary>
[McpServerToolType]
public sealed class ServiceMapTools
{
    private readonly IServiceMapService _service;

    /// <summary>Creates service-map MCP tools.</summary>
    public ServiceMapTools(IServiceMapService service) => _service = service;

    /// <summary>Map HTTP/config/build dependencies across collections.</summary>
    [McpServerTool(Name = "map_service_dependencies"), Description(
        "Automatically discover and map HTTP service dependencies across indexed " +
        "collections. Scans for endpoint definitions, HTTP client calls, service " +
        "configuration, and build-level dependencies (Maven, NuGet, npm, Gradle, Go, Cargo, Python). " +
        "Returns a dependency graph with matched endpoint paths and package references. " +
        "When RERANK_ENABLED=true, pass rerank=false to skip ColBERT (Phase 6; currently no-op).")]
    public Task<object> MapServiceDependenciesAsync(
        [Description("Collections to analyze; omit for all")] string[]? collections = null,
        [Description("Max results per discovery query per collection")] int top_k = 30,
        [Description("ColBERT rerank override (ignored until Phase 6)")] bool? rerank = null,
        CancellationToken cancellationToken = default) =>
        _service.MapServiceDependenciesAsync(collections, top_k, rerank, cancellationToken);
}
