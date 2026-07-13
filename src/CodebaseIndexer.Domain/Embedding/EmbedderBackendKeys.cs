namespace CodebaseIndexer.Domain.Embedding;

/// <summary>Known backend key constants for dense and sparse embedders.</summary>
public static class EmbedderBackendKeys
{
    /// <summary>Backend keys for dense embedding providers.</summary>
    public static class Dense
    {
        /// <summary>Text Embeddings Inference (TEI) dense embedding backend.</summary>
        public const string Tei = "tei";
    }

    /// <summary>Backend keys for sparse embedding providers.</summary>
    public static class Sparse
    {
        /// <summary>ONNX runtime sparse embedding backend.</summary>
        public const string Onnx = "onnx";
    }
}
