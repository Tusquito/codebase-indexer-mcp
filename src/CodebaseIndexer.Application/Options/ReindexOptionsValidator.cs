using FluentValidation;

namespace CodebaseIndexer.Application.Options;

/// <summary>FluentValidation rules for <see cref="ReindexOptions"/>.</summary>
public sealed class ReindexOptionsValidator : AbstractValidator<ReindexOptions>
{
    /// <summary>Creates validation rules for scheduled reindex settings.</summary>
    public ReindexOptionsValidator()
    {
        RuleFor(x => x.IndexTimeoutSeconds)
            .GreaterThan(0)
            .WithMessage($"{nameof(ReindexOptions.IndexTimeoutSeconds)} must be positive.");

        RuleFor(x => x.GitTimeoutSeconds)
            .GreaterThan(0)
            .WithMessage($"{nameof(ReindexOptions.GitTimeoutSeconds)} must be positive.");

        RuleFor(x => x)
            .Must(x => !x.Enabled
                || !string.IsNullOrWhiteSpace(x.Cron)
                || !string.IsNullOrWhiteSpace(x.Interval))
            .WithMessage("Reindex requires Cron or Interval when Enabled.");
    }
}
