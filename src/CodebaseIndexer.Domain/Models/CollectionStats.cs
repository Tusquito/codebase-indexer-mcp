namespace CodebaseIndexer.Domain.Models;

/// <summary>Statistics and configuration metadata for a vector collection.</summary>
/// <param name="Name">Collection name.</param>
/// <param name="VectorCount">Number of vectors stored in the collection.</param>
/// <param name="DiskSizeMb">Approximate on-disk size in megabytes.</param>
/// <param name="DenseEmbedModel">Dense embedding model identifier.</param>
/// <param name="SparseEmbedModel">Sparse embedding model identifier.</param>
/// <param name="DenseEmbedBackend">Backend key used for dense embeddings.</param>
/// <param name="Hybrid">Whether hybrid dense and sparse search is enabled.</param>
/// <param name="RerankEnabled">Whether result reranking is enabled.</param>
/// <param name="ColbertEmbedModel">ColBERT embedding model identifier, if configured.</param>
/// <param name="GraphCallSites">Whether call-site graph edges are indexed.</param>
/// <param name="GraphEnabled">Whether the graph store integration is enabled.</param>
public sealed record CollectionStats(
    string Name,
    long VectorCount,
    double DiskSizeMb,
    string DenseEmbedModel,
    string SparseEmbedModel,
    string DenseEmbedBackend,
    bool Hybrid,
    bool RerankEnabled = false,
    string ColbertEmbedModel = "",
    bool GraphCallSites = false,
    bool GraphEnabled = false)
{
    /// <summary>Collection name.</summary>
    public string Name { get; init; } = Name;

    /// <summary>Number of vectors stored in the collection.</summary>
    public long VectorCount { get; init; } = VectorCount;

    /// <summary>Approximate on-disk size in megabytes.</summary>
    public double DiskSizeMb { get; init; } = DiskSizeMb;

    /// <summary>Dense embedding model identifier.</summary>
    public string DenseEmbedModel { get; init; } = DenseEmbedModel;

    /// <summary>Sparse embedding model identifier.</summary>
    public string SparseEmbedModel { get; init; } = SparseEmbedModel;

    /// <summary>Backend key used for dense embeddings.</summary>
    public string DenseEmbedBackend { get; init; } = DenseEmbedBackend;

    /// <summary>Whether hybrid dense and sparse search is enabled.</summary>
    public bool Hybrid { get; init; } = Hybrid;

    /// <summary>Whether result reranking is enabled.</summary>
    public bool RerankEnabled { get; init; } = RerankEnabled;

    /// <summary>ColBERT embedding model identifier, if configured.</summary>
    public string ColbertEmbedModel { get; init; } = ColbertEmbedModel;

    /// <summary>Whether call-site graph edges are indexed.</summary>
    public bool GraphCallSites { get; init; } = GraphCallSites;

    /// <summary>Whether the graph store integration is enabled.</summary>
    public bool GraphEnabled { get; init; } = GraphEnabled;
}
