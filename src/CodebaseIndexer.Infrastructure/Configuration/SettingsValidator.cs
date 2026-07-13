using FluentValidation;

namespace CodebaseIndexer.Infrastructure.Configuration;

public sealed class SettingsValidator : AbstractValidator<Settings>
{
    public SettingsValidator()
    {
        RuleFor(x => x.QdrantUrl)
            .NotEmpty()
            .WithMessage($"{nameof(Settings.QdrantUrl)} is required.");

        RuleFor(x => x.TeiUrl)
            .NotEmpty()
            .WithMessage($"{nameof(Settings.TeiUrl)} is required.");

        RuleFor(x => x.QdrantCollection)
            .NotEmpty()
            .WithMessage($"{nameof(Settings.QdrantCollection)} is required.");

        RuleFor(x => x.DenseEmbedModel)
            .NotEmpty()
            .WithMessage($"{nameof(Settings.DenseEmbedModel)} is required.");

        RuleFor(x => x.SparseEmbedModel)
            .NotEmpty()
            .WithMessage($"{nameof(Settings.SparseEmbedModel)} is required.");

        RuleFor(x => x.DenseEmbedVectorSize)
            .GreaterThan(0)
            .WithMessage($"{nameof(Settings.DenseEmbedVectorSize)} must be positive.");

        RuleFor(x => x.QdrantTimeoutSeconds)
            .GreaterThan(0)
            .WithMessage($"{nameof(Settings.QdrantTimeoutSeconds)} must be positive.");

        RuleFor(x => x.TeiTimeoutSeconds)
            .GreaterThan(0)
            .WithMessage($"{nameof(Settings.TeiTimeoutSeconds)} must be positive.");

        RuleFor(x => x.TeiEmbedBatchSize)
            .GreaterThan(0)
            .WithMessage($"{nameof(Settings.TeiEmbedBatchSize)} must be positive.");
    }
}
