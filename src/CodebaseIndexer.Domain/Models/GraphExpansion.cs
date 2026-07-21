namespace CodebaseIndexer.Domain.Models;

/// <summary>Bounded neighborhood returned by graph subgraph expansion.</summary>
/// <param name="Nodes">Expanded nodes.</param>
/// <param name="Edges">Expanded edges.</param>
/// <param name="RelatedChunkIds">Non-seed chunk ids discovered in the neighborhood.</param>
/// <param name="RelatedChunkCollections">chunk_id → owning collection for Qdrant hydration.</param>
public sealed record GraphExpansion(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<string> RelatedChunkIds,
    IReadOnlyDictionary<string, string> RelatedChunkCollections)
{
    /// <summary>Empty expansion (graph disabled or no seeds).</summary>
    public static GraphExpansion Empty { get; } = new(
        Array.Empty<GraphNode>(),
        Array.Empty<GraphEdge>(),
        Array.Empty<string>(),
        new Dictionary<string, string>());
}
