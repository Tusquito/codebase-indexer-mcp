namespace CodebaseIndexer.Infrastructure.Embedding;

/// <summary>Source of the resolved embedding token limit.</summary>
public enum TruncationSource
{
    /// <summary>Explicit environment or configuration override.</summary>
    EnvOverride,

    /// <summary>Detected from model files or known-model registry.</summary>
    ModelAutoDetect,

    /// <summary>Truncation disabled (no limit applied).</summary>
    Disabled,
}
