using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Options;

/// <summary>
/// ADR 0022 Phase 2 parity: when rerank is on and Colbert:EmbedBackend is unset, default to remote.
/// </summary>
public sealed class ColbertBackendPostConfigure : IPostConfigureOptions<ColbertOptions>
{
    private readonly IConfiguration _configuration;

    /// <summary>Creates the post-configure hook.</summary>
    public ColbertBackendPostConfigure(IConfiguration configuration) =>
        _configuration = configuration;

    /// <inheritdoc />
    public void PostConfigure(string? name, ColbertOptions options)
    {
        var rerank = _configuration.GetValue($"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.RerankEnabled)}", false);
        if (rerank && string.IsNullOrWhiteSpace(options.EmbedBackend))
        {
            options.EmbedBackend = "remote";
        }
        else if (string.IsNullOrWhiteSpace(options.EmbedBackend))
        {
            options.EmbedBackend = "onnx";
        }
    }
}
