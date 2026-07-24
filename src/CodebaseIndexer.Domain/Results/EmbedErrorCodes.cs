namespace CodebaseIndexer.Domain.Results;

/// <summary>
/// Stable machine-readable error codes for embedding ports (ADR 0033 Phase 3).
/// Callers should treat unknown codes as opaque.
/// </summary>
public static class EmbedErrorCodes
{
    /// <summary>TEI dense embedding request failed.</summary>
    public const string Tei = "embed.tei";

    /// <summary>Sparse ONNX/BM25 embedding failed.</summary>
    public const string Sparse = "embed.sparse";

    /// <summary>ColBERT embedding (ONNX or remote) failed.</summary>
    public const string Colbert = "embed.colbert";

    /// <summary>Embedding dimension does not match configured vector size.</summary>
    public const string DimensionMismatch = "embed.dimension_mismatch";

    /// <summary>Requested embedder is not configured (e.g. ColBERT when rerank disabled).</summary>
    public const string NotConfigured = "embed.not_configured";
}
