using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Options;

/// <summary>Caps Indexing:UpsertBatch when ColBERT rerank is enabled.</summary>
public sealed class IndexingUpsertBatchValidateOptions : IValidateOptions<IndexingOptions>
{
    private readonly IOptions<EmbeddingOptions> _embedding;

    /// <summary>Creates the cross-option validator.</summary>
    public IndexingUpsertBatchValidateOptions(IOptions<EmbeddingOptions> embedding) =>
        _embedding = embedding;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, IndexingOptions options)
    {
        if (_embedding.Value.RerankEnabled && options.UpsertBatch > 25)
        {
            return ValidateOptionsResult.Fail(
                "Indexing:UpsertBatch must be <= 25 when Embedding:RerankEnabled=true (ColBERT multivector payloads).");
        }

        return ValidateOptionsResult.Success;
    }
}
