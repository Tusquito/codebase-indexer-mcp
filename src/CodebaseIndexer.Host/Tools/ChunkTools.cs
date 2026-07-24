using System.ComponentModel;
using CodebaseIndexer.Application.Mapping;
using CodebaseIndexer.Application.Services;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tool: get_chunk.</summary>
[McpServerToolType]
public sealed class ChunkTools
{
    private readonly ICollectionQueryService _queries;

    /// <summary>Creates chunk tools.</summary>
    public ChunkTools(ICollectionQueryService queries) => _queries = queries;

    /// <summary>Retrieve a specific chunk by ID from a prior search result.</summary>
    [McpServerTool(Name = "get_chunk"), Description("Retrieve a specific chunk by ID from a prior search result.")]
    public async Task<object> GetChunkAsync(
        [Description("Chunk id from a prior search result")] string chunk_id,
        [Description("Optional collection scope")] string? collection = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _queries.GetChunkAsync(chunk_id, collection, cancellationToken).ConfigureAwait(false);
        return result.Match(v => v, McpErrorMapper.FromError);
    }
}
