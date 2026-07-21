namespace CodebaseIndexer.Domain.Models;

/// <summary>A positive or negative example for Qdrant Recommend (point id or dense vector).</summary>
public sealed class RecommendExample
{
    private RecommendExample(string? chunkId, IReadOnlyList<float>? denseVector)
    {
        ChunkId = chunkId;
        DenseVector = denseVector;
    }

    /// <summary>Chunk id when the example is an indexed chunk (store maps to point UUID).</summary>
    public string? ChunkId { get; }

    /// <summary>Dense embedding when the example is a free-text query.</summary>
    public IReadOnlyList<float>? DenseVector { get; }

    /// <summary>Creates an example from an indexed chunk id.</summary>
    public static RecommendExample FromChunkId(string chunkId) => new(chunkId, null);

    /// <summary>Creates an example from a dense vector.</summary>
    public static RecommendExample FromDenseVector(IReadOnlyList<float> denseVector) => new(null, denseVector);
}
