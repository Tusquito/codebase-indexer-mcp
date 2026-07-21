namespace CodebaseIndexer.Application.Options;

/// <summary>Configuration for dense and sparse embedding models.</summary>
public sealed class EmbeddingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Embedding";

    /// <summary>Whether hybrid dense+sparse search is enabled.</summary>
    public bool HybridSearch { get; init; }

    /// <summary>Dense embedding model identifier.</summary>
    public string DenseModel { get; init; } = string.Empty;

    /// <summary>Sparse embedding model identifier.</summary>
    public string SparseModel { get; init; } = string.Empty;

    /// <summary>Dimensionality of dense vectors.</summary>
    public int DenseVectorSize { get; init; }

    /// <summary>Whether ColBERT reranking is enabled after hybrid retrieval.</summary>
    public bool RerankEnabled { get; init; }

    /// <summary>ColBERT model id (mirrors Colbert:EmbedModel; used for dim registry).</summary>
    public string ColbertEmbedModel { get; init; } = "colbert-ir/colbertv2.0";

    /// <summary>Hybrid candidate pool size before ColBERT MAX_SIM (not top_k).</summary>
    public int RerankPrefetch { get; init; } = 100;

    /// <summary>Query token cap for ColBERT (0 = model default).</summary>
    public int RerankMaxQueryTokens { get; init; }

    /// <summary>Skip ColBERT when hybrid RRF gap is large enough.</summary>
    public bool RerankAdaptiveEnabled { get; set; } = true;

    /// <summary>RRF score gap threshold for adaptive ColBERT skip.</summary>
    public float RerankAdaptiveGap { get; init; } = 0.02f;

    /// <summary>Maximum token count for dense embedding input.</summary>
    public int MaxDenseTokens { get; init; }

    /// <summary>Maximum token count for sparse embedding input.</summary>
    public int MaxSparseTokens { get; init; }

    /// <summary>Filesystem path for embedding model cache.</summary>
    public string CachePath { get; init; } = string.Empty;

    /// <summary>Thread count for sparse / ColBERT ONNX inference.</summary>
    public int SparseThreads { get; init; }

    /// <summary>Hybrid prefetch limit multiplier (<c>top_k * multiplier</c>).</summary>
    public int PrefetchMultiplier { get; init; } = 5;

    /// <summary>RRF constant for cross-collection fusion (default 60).</summary>
    public int RrfK { get; init; } = 60;
}
