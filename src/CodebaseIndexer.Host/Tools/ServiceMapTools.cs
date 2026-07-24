using System.ComponentModel;
using CodebaseIndexer.Application.Mapping;
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
        "When Embedding:RerankEnabled=true, pass rerank=false to skip ColBERT on discovery search.")]
    public async Task<object> MapServiceDependenciesAsync(
        [Description("Collections to analyze; omit for all")] string[]? collections = null,
        [Description("Max results per discovery query per collection")] int top_k = 30,
        [Description("ColBERT override: false skips rerank when enabled")] bool? rerank = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.MapServiceDependenciesAsync(collections, top_k, rerank, cancellationToken)
            .ConfigureAwait(false);
        return result.Match(v => v, McpErrorMapper.FromError);
    }
}
