using FluentValidation;

namespace CodebaseIndexer.Application.Options;

/// <summary>FluentValidation rules for <see cref="EmbeddingOptions"/>.</summary>
public sealed class EmbeddingOptionsValidator : AbstractValidator<EmbeddingOptions>
{
    /// <summary>Creates validation rules for required embedding settings.</summary>
    public EmbeddingOptionsValidator()
    {
        RuleFor(x => x.DenseModel)
            .NotEmpty()
            .WithMessage($"{nameof(EmbeddingOptions.DenseModel)} is required.");

        RuleFor(x => x.SparseModel)
            .NotEmpty()
            .WithMessage($"{nameof(EmbeddingOptions.SparseModel)} is required.");

        RuleFor(x => x.DenseVectorSize)
            .GreaterThan(0)
            .WithMessage($"{nameof(EmbeddingOptions.DenseVectorSize)} must be positive.");

        RuleFor(x => x.CachePath)
            .NotEmpty()
            .WithMessage($"{nameof(EmbeddingOptions.CachePath)} is required.");

        RuleFor(x => x.PrefetchMultiplier)
            .GreaterThanOrEqualTo(1)
            .WithMessage($"{nameof(EmbeddingOptions.PrefetchMultiplier)} must be >= 1.");

        RuleFor(x => x.RrfK)
            .GreaterThanOrEqualTo(1)
            .WithMessage($"{nameof(EmbeddingOptions.RrfK)} must be >= 1.");

        RuleFor(x => x.RerankPrefetch)
            .GreaterThanOrEqualTo(1)
            .WithMessage($"{nameof(EmbeddingOptions.RerankPrefetch)} must be >= 1.");

        RuleFor(x => x.RerankAdaptiveGap)
            .GreaterThanOrEqualTo(0)
            .WithMessage($"{nameof(EmbeddingOptions.RerankAdaptiveGap)} must be >= 0.");

        RuleFor(x => x)
            .Must(x => !x.RerankEnabled || x.HybridSearch)
            .WithMessage("Embedding:RerankEnabled requires Embedding:HybridSearch.");

        RuleFor(x => x.ColbertEmbedModel)
            .NotEmpty()
            .When(x => x.RerankEnabled)
            .WithMessage($"{nameof(EmbeddingOptions.ColbertEmbedModel)} is required when rerank is enabled.");
    }
}
