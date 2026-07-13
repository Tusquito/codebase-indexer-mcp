using FluentValidation;

namespace CodebaseIndexer.Infrastructure.Configuration;

/// <summary>FluentValidation rules for <see cref="TeiOptions"/>.</summary>
public sealed class TeiOptionsValidator : AbstractValidator<TeiOptions>
{
    /// <summary>Initializes validation rules for TEI options.</summary>
    public TeiOptionsValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty()
            .WithMessage($"{nameof(TeiOptions.Url)} is required.");

        RuleFor(x => x.TimeoutSeconds)
            .GreaterThan(0)
            .WithMessage($"{nameof(TeiOptions.TimeoutSeconds)} must be positive.");

        RuleFor(x => x.EmbedBatchSize)
            .GreaterThan(0)
            .WithMessage($"{nameof(TeiOptions.EmbedBatchSize)} must be positive.");
    }
}
