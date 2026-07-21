namespace CodebaseIndexer.Domain.Models;

/// <summary>A Neo4j node surfaced by subgraph expansion.</summary>
/// <param name="Labels">Node labels.</param>
/// <param name="Key">Stable human-readable key when available.</param>
/// <param name="Props">Bounded property subset for MCP responses.</param>
public sealed record GraphNode(
    IReadOnlyList<string> Labels,
    string? Key,
    IReadOnlyDictionary<string, object?> Props);
