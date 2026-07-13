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
    }
}
