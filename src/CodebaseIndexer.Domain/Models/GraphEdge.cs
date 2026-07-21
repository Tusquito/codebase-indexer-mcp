namespace CodebaseIndexer.Domain.Models;

/// <summary>A Neo4j relationship surfaced by subgraph expansion.</summary>
/// <param name="Type">Relationship type.</param>
/// <param name="FromKey">Source node key.</param>
/// <param name="ToKey">Target node key.</param>
public sealed record GraphEdge(
    string Type,
    string? FromKey,
    string? ToKey);
