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

        RuleFor(x => x.HnswEf)
            .GreaterThanOrEqualTo(1)
            .WithMessage($"{nameof(QdrantOptions.HnswEf)} must be >= 1.");

        RuleFor(x => x.HnswM)
            .GreaterThanOrEqualTo(1)
            .WithMessage($"{nameof(QdrantOptions.HnswM)} must be >= 1.");

        RuleFor(x => x.HnswEfConstruct)
            .GreaterThanOrEqualTo(1)
            .WithMessage($"{nameof(QdrantOptions.HnswEfConstruct)} must be >= 1.");

        RuleFor(x => x.QuantOversampling)
            .GreaterThan(0)
            .WithMessage($"{nameof(QdrantOptions.QuantOversampling)} must be > 0.");

        RuleFor(x => x.MemmapThresholdKb)
            .GreaterThanOrEqualTo(0)
            .WithMessage($"{nameof(QdrantOptions.MemmapThresholdKb)} must be >= 0.");
    }
}
