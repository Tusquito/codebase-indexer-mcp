using FluentValidation;

namespace CodebaseIndexer.Application.Options;

/// <summary>FluentValidation rules for <see cref="DiscoveryOptions"/>.</summary>
public sealed class DiscoveryOptionsValidator : AbstractValidator<DiscoveryOptions>
{
    /// <summary>Creates validation rules for discovery settings.</summary>
    public DiscoveryOptionsValidator()
    {
        RuleFor(x => x.RecommendMaxExamples)
            .GreaterThanOrEqualTo(1)
            .WithMessage($"{nameof(DiscoveryOptions.RecommendMaxExamples)} must be >= 1.");

        RuleFor(x => x.OutlierMaxContextSamples)
            .GreaterThanOrEqualTo(1)
            .WithMessage($"{nameof(DiscoveryOptions.OutlierMaxContextSamples)} must be >= 1.");

        RuleFor(x => x.OutlierMaxSimilarity)
            .InclusiveBetween(0f, 1f)
            .WithMessage($"{nameof(DiscoveryOptions.OutlierMaxSimilarity)} must be between 0 and 1.");
    }
}
