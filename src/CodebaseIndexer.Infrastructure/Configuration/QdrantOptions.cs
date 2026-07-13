namespace CodebaseIndexer.Infrastructure.Configuration;

/// <summary>Configuration for the Qdrant vector database connection.</summary>
public sealed class QdrantOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Qdrant";

    /// <summary>Qdrant gRPC/HTTP endpoint URL.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Client connection timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>Default collection name.</summary>
    public string Collection { get; init; } = string.Empty;

    /// <summary>Whether to create payload field indexes on collection setup.</summary>
    public bool PayloadIndexes { get; init; }

    /// <summary>Whether dense vectors are stored on disk.</summary>
    public bool VectorsOnDisk { get; init; }

    /// <summary>Whether sparse vector indexes are stored on disk.</summary>
    public bool SparseOnDisk { get; init; }
}
