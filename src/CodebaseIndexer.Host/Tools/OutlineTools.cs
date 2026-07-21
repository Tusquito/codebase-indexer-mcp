using System.ComponentModel;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tool: get_file_outline.</summary>
[McpServerToolType]
public sealed class OutlineTools
{
    private readonly ICollectionQueryService _queries;
    private readonly QdrantOptions _qdrant;

    /// <summary>Creates outline tools.</summary>
    public OutlineTools(ICollectionQueryService queries, IOptions<QdrantOptions> qdrant)
    {
        _queries = queries;
        _qdrant = qdrant.Value;
    }

    /// <summary>Return the symbol tree of a specific file — no code content.</summary>
    [McpServerTool(Name = "get_file_outline"), Description(
        "Return the symbol tree of a specific file — no code content returned. " +
        "Lists all symbols with type and line numbers. Zero embedding cost.")]
    public Task<object> GetFileOutlineAsync(
        [Description("Repository-relative path")] string rel_path,
        [Description("Collection name")] string? collection = null,
        CancellationToken cancellationToken = default) =>
        _queries.GetFileOutlineAsync(rel_path, collection ?? _qdrant.Collection, cancellationToken);
}
