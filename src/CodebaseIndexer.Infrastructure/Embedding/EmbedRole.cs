namespace CodebaseIndexer.Infrastructure.Embedding;

/// <summary>Embedding pipeline role used when resolving token limits.</summary>
public enum EmbedRole
{
    /// <summary>Dense vector embedding via TEI.</summary>
    Dense,

    /// <summary>Sparse BM25-style embedding via ONNX.</summary>
    Sparse,
}
