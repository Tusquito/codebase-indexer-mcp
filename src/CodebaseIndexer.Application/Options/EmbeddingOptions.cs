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

    /// <summary>Whether reranking is enabled after retrieval.</summary>
    public bool RerankEnabled { get; init; }

    /// <summary>Maximum token count for dense embedding input.</summary>
    public int MaxDenseTokens { get; init; }

    /// <summary>Maximum token count for sparse embedding input.</summary>
    public int MaxSparseTokens { get; init; }

    /// <summary>Filesystem path for embedding model cache.</summary>
    public string CachePath { get; init; } = string.Empty;

    /// <summary>Thread count for sparse embedding inference.</summary>
    public int SparseThreads { get; init; }

    /// <summary>Hybrid prefetch limit multiplier (<c>top_k * multiplier</c>).</summary>
    public int PrefetchMultiplier { get; init; } = 5;

    /// <summary>RRF constant for cross-collection fusion (default 60).</summary>
    public int RrfK { get; init; } = 60;
}
