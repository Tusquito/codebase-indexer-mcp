namespace CodebaseIndexer.Infrastructure.Configuration;

/// <summary>Configuration for the Qdrant vector database connection.</summary>
public sealed class QdrantOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Qdrant";

    /// <summary>
    /// Qdrant endpoint URL for <c>Qdrant.Client</c> (gRPC). Prefer port 6334 (Qdrant gRPC default).
    /// Port 6333 (REST) is remapped to 6334 at connect time.
    /// </summary>
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

    /// <summary>Enable int8 scalar quantization on collection create.</summary>
    public bool Quantization { get; init; } = true;

    /// <summary>Query-time HNSW ef (search breadth).</summary>
    public int HnswEf { get; init; } = 64;

    /// <summary>HNSW graph degree (m) at build time.</summary>
    public int HnswM { get; init; } = 16;

    /// <summary>HNSW construction search breadth.</summary>
    public int HnswEfConstruct { get; init; } = 128;

    /// <summary>Oversampling factor for quantized search rescoring.</summary>
    public double QuantOversampling { get; init; } = 2.0;

    /// <summary>Optimizer memmap threshold in KB.</summary>
    public int MemmapThresholdKb { get; init; } = 20_000;
}
