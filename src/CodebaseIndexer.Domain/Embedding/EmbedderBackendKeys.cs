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

    /// <summary>Backend keys for ColBERT late-interaction embedders.</summary>
    public static class Colbert
    {
        /// <summary>In-process ONNX ColBERT backend.</summary>
        public const string Onnx = "colbert-onnx";

        /// <summary>Remote HTTP ColBERT sidecar backend.</summary>
        public const string Remote = "colbert-remote";
    }
}
