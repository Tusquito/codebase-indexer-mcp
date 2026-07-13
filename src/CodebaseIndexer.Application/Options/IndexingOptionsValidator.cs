using FluentValidation;

namespace CodebaseIndexer.Application.Options;

/// <summary>FluentValidation rules for <see cref="IndexingOptions"/>.</summary>
public sealed class IndexingOptionsValidator : AbstractValidator<IndexingOptions>
{
    /// <summary>Creates validation rules for pipeline batch settings.</summary>
    public IndexingOptionsValidator()
    {
        RuleFor(x => x.FlushEvery)
            .GreaterThan(0)
            .WithMessage($"{nameof(IndexingOptions.FlushEvery)} must be positive.");

        RuleFor(x => x.UpsertBatch)
            .GreaterThan(0)
            .WithMessage($"{nameof(IndexingOptions.UpsertBatch)} must be positive.");
    }
}
