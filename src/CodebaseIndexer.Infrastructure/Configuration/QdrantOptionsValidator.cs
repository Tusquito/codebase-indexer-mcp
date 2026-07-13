using FluentValidation;

namespace CodebaseIndexer.Infrastructure.Configuration;

/// <summary>FluentValidation rules for <see cref="QdrantOptions"/>.</summary>
public sealed class QdrantOptionsValidator : AbstractValidator<QdrantOptions>
{
    /// <summary>Initializes validation rules for Qdrant options.</summary>
    public QdrantOptionsValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty()
            .WithMessage($"{nameof(QdrantOptions.Url)} is required.");

        RuleFor(x => x.Collection)
            .NotEmpty()
            .WithMessage($"{nameof(QdrantOptions.Collection)} is required.");

        RuleFor(x => x.TimeoutSeconds)
            .GreaterThan(0)
            .WithMessage($"{nameof(QdrantOptions.TimeoutSeconds)} must be positive.");
    }
}
