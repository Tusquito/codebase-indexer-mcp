using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Options;

/// <summary>Forces adaptive ColBERT skip off when rerank is disabled.</summary>
public sealed class EmbeddingAdaptivePostConfigure : IPostConfigureOptions<EmbeddingOptions>
{
    /// <inheritdoc />
    public void PostConfigure(string? name, EmbeddingOptions options)
    {
        if (!options.RerankEnabled)
        {
            options.RerankAdaptiveEnabled = false;
        }
    }
}
