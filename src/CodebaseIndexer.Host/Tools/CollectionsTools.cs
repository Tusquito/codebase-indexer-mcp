using System.ComponentModel;
using CodebaseIndexer.Application.Mapping;
using CodebaseIndexer.Application.Services;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tool: list_collections.</summary>
[McpServerToolType]
public sealed class CollectionsTools
{
    private readonly ICollectionQueryService _queries;

    /// <summary>Creates collections tools.</summary>
    public CollectionsTools(ICollectionQueryService queries) => _queries = queries;

    /// <summary>List all indexed collections with statistics.</summary>
    [McpServerTool(Name = "list_collections"), Description("List all indexed collections with statistics.")]
    public async Task<object> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _queries.ListCollectionsAsync(cancellationToken).ConfigureAwait(false);
        return result.Match(v => (object)v, McpErrorMapper.FromError);
    }
}
