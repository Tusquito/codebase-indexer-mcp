namespace CodebaseIndexer.Infrastructure.Configuration;

/// <summary>Configuration for the Text Embeddings Inference (TEI) HTTP service.</summary>
public sealed class TeiOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Tei";

    /// <summary>Base URL of the TEI server.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Maximum texts per embedding request batch.</summary>
    public int EmbedBatchSize { get; init; }

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>Optional Matryoshka representation learning output dimensions.</summary>
    public int? MrlDimensions { get; init; }

    /// <summary>Prefix prepended to query texts before embedding.</summary>
    public string QueryInstruction { get; init; } = string.Empty;

    /// <summary>Whether to L2-normalize dense embedding vectors.</summary>
    public bool NormalizeOutput { get; init; }
}
