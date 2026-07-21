namespace CodebaseIndexer.Application.Options;

/// <summary>Configuration for ColBERT late-interaction embedding.</summary>
public sealed class ColbertOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Colbert";

    /// <summary>ColBERT model identifier (token dimension registry key).</summary>
    public string EmbedModel { get; init; } = "colbert-ir/colbertv2.0";

    /// <summary>
    /// Backend: <c>onnx</c>, <c>remote</c>, or empty to resolve from
    /// <see cref="EmbeddingOptions.RerankEnabled"/> (remote when rerank on).
    /// </summary>
    public string EmbedBackend { get; set; } = string.Empty;

    /// <summary>Base URL of the ColBERT HTTP sidecar.</summary>
    public string Url { get; init; } = "http://colbert:8082";

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>Maximum texts per ColBERT embed batch.</summary>
    public int EmbedBatchSize { get; init; } = 16;

    /// <summary>Prefer CUDA execution provider (fail-fast when unavailable).</summary>
    public bool UseCuda { get; init; }

    /// <summary>Optional ORT CUDA device ids (comma-separated in env).</summary>
    public string DeviceIds { get; init; } = string.Empty;

    /// <summary>
    /// ORT CUDA <c>gpu_mem_limit</c> in bytes. Default 2 GiB leaves headroom when
    /// ColBERT shares a GPU with other workloads (e.g. TEI). Set 0 to omit the limit
    /// (ORT default = no cap).
    /// </summary>
    public long GpuMemLimitBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Query/doc token cap (0 = model default).</summary>
    public int MaxQueryTokens { get; init; }

    /// <summary>Resolved backend after post-configure (<c>onnx</c> or <c>remote</c>).</summary>
    public string ResolvedEmbedBackend =>
        string.IsNullOrWhiteSpace(EmbedBackend) ? "onnx" : EmbedBackend.Trim().ToLowerInvariant();
}
