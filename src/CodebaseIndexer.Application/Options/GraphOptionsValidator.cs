using FluentValidation;

namespace CodebaseIndexer.Application.Options;

/// <summary>FluentValidation rules for <see cref="GraphOptions"/>.</summary>
public sealed class GraphOptionsValidator : AbstractValidator<GraphOptions>
{
    /// <summary>Creates validation rules for graph settings.</summary>
    public GraphOptionsValidator()
    {
        When(x => x.Enabled, () =>
        {
            RuleFor(x => x.Neo4jPassword)
                .Must(p => !string.IsNullOrWhiteSpace(p))
                .WithMessage($"{nameof(GraphOptions.Neo4jPassword)} is required when Graph:Enabled is true.");
        });

        RuleFor(x => x.WriterBatch)
            .GreaterThanOrEqualTo(1)
            .WithMessage($"{nameof(GraphOptions.WriterBatch)} must be >= 1.");

        RuleFor(x => x.MaxHops)
            .GreaterThanOrEqualTo(1)
            .WithMessage($"{nameof(GraphOptions.MaxHops)} must be >= 1.");

        RuleFor(x => x.MaxNodes)
            .GreaterThanOrEqualTo(1)
            .WithMessage($"{nameof(GraphOptions.MaxNodes)} must be >= 1.");
    }
}
