namespace CodebaseIndexer.Domain.Models;

/// <summary>A code chunk with its dense and optional sparse embedding vectors.</summary>
/// <param name="Chunk">Source chunk metadata and text.</param>
/// <param name="DenseVector">Dense embedding vector for semantic search.</param>
/// <param name="SparseVector">Optional sparse embedding vector for hybrid search.</param>
public sealed record EmbeddedChunk(
    Chunk Chunk,
    IReadOnlyList<float> DenseVector,
    SparseVector? SparseVector)
{
    /// <summary>Source chunk metadata and text.</summary>
    public Chunk Chunk { get; init; } = Chunk;

    /// <summary>Dense embedding vector for semantic search.</summary>
    public IReadOnlyList<float> DenseVector { get; init; } = DenseVector;

    /// <summary>Optional sparse embedding vector for hybrid search.</summary>
    public SparseVector? SparseVector { get; init; } = SparseVector;
}
