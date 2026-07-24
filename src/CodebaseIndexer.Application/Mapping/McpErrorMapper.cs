using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Application.Mapping;

/// <summary>Maps Domain <see cref="Error"/> values to the unified MCP failure envelope.</summary>
public static class McpErrorMapper
{
    /// <summary>Creates the Host/MCP error payload from a typed <see cref="Error"/>.</summary>
    public static object FromError(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new McpErrorEnvelope(
            new McpErrorBody(error.Kind, error.Code, error.Message, error.Metadata));
    }
}
