using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Options;

/// <summary>Requires Colbert:Url when rerank uses the remote backend.</summary>
public sealed class ColbertRemoteUrlValidateOptions : IValidateOptions<ColbertOptions>
{
    private readonly IOptions<EmbeddingOptions> _embedding;

    /// <summary>Creates the cross-option validator.</summary>
    public ColbertRemoteUrlValidateOptions(IOptions<EmbeddingOptions> embedding) =>
        _embedding = embedding;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ColbertOptions options)
    {
        var embedding = _embedding.Value;
        if (!embedding.RerankEnabled)
        {
            return ValidateOptionsResult.Success;
        }

        var backend = string.IsNullOrWhiteSpace(options.EmbedBackend)
            ? "remote"
            : options.EmbedBackend.Trim().ToLowerInvariant();

        if (backend == "remote" && string.IsNullOrWhiteSpace(options.Url))
        {
            return ValidateOptionsResult.Fail(
                "Colbert:Url is required when Embedding:RerankEnabled=true and Colbert:EmbedBackend=remote.");
        }

        return ValidateOptionsResult.Success;
    }
}
