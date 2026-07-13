namespace CodebaseIndexer.Application.Options;

/// <summary>Configuration for the indexing pipeline.</summary>
public sealed class IndexingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Indexing";

    /// <summary>Force sequential embedding instead of parallel dense/sparse.</summary>
    public bool SequentialEmbed { get; init; }

    /// <summary>Memory usage percentage that triggers a warning.</summary>
    public int MemoryPressureWarnPct { get; init; }

    /// <summary>Memory usage percentage that halts embedding.</summary>
    public int MemoryPressureHaltPct { get; init; }

    /// <summary>Release embedding models after each index run.</summary>
    public bool ReleaseModelsAfterIndex { get; init; }

    /// <summary>Chunk count threshold before flushing to the embed stage.</summary>
    public int FlushEvery { get; init; }

    /// <summary>Maximum embedded chunks per vector-store upsert batch.</summary>
    public int UpsertBatch { get; init; }

    /// <summary>Default batch size for embedding requests.</summary>
    public int BatchSize { get; init; }

    /// <summary>Preload embedding models at startup.</summary>
    public bool PreloadModels { get; init; }

    /// <summary>Seconds of idle time before unloading models.</summary>
    public int ModelIdleTimeoutSeconds { get; init; }
}
