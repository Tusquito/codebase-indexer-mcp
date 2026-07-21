using System.ComponentModel;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tool: get_collection_summary.</summary>
[McpServerToolType]
public sealed class SummaryTools
{
    private readonly ICollectionQueryService _queries;
    private readonly QdrantOptions _qdrant;

    /// <summary>Creates summary tools.</summary>
    public SummaryTools(ICollectionQueryService queries, IOptions<QdrantOptions> qdrant)
    {
        _queries = queries;
        _qdrant = qdrant.Value;
    }

    /// <summary>Compact codebase orientation — no embedding cost.</summary>
    [McpServerTool(Name = "get_collection_summary"), Description(
        "Compact codebase orientation in a single tool call — no embedding cost. " +
        "Returns file counts, language breakdown, directory tree, symbol types, top chunked files, " +
        "and build_dependencies when other indexed collections match manifest artifacts.")]
    public Task<object> GetCollectionSummaryAsync(
        [Description("Collection name")] string? collection = null,
        CancellationToken cancellationToken = default) =>
        _queries.GetCollectionSummaryAsync(collection ?? _qdrant.Collection, cancellationToken);
}
