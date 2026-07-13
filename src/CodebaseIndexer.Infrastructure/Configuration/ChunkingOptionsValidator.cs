using FluentValidation;

namespace CodebaseIndexer.Infrastructure.Configuration;

/// <summary>Validates <see cref="ChunkingOptions"/> at startup.</summary>
public sealed class ChunkingOptionsValidator : AbstractValidator<ChunkingOptions>
{
    /// <summary>Creates validation rules for chunking options.</summary>
    public ChunkingOptionsValidator()
    {
        RuleFor(x => x.MaxLines)
            .GreaterThan(0)
            .WithMessage($"{nameof(ChunkingOptions.MaxLines)} must be positive.");

        RuleFor(x => x.OverlapLines)
            .GreaterThanOrEqualTo(0)
            .WithMessage($"{nameof(ChunkingOptions.OverlapLines)} must be non-negative.");
    }
}
