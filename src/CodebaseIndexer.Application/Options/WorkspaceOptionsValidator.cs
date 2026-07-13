using FluentValidation;

namespace CodebaseIndexer.Application.Options;

/// <summary>FluentValidation rules for <see cref="WorkspaceOptions"/>.</summary>
public sealed class WorkspaceOptionsValidator : AbstractValidator<WorkspaceOptions>
{
    /// <summary>Creates validation rules for required workspace settings.</summary>
    public WorkspaceOptionsValidator()
    {
        RuleFor(x => x.Path)
            .NotEmpty()
            .WithMessage($"{nameof(WorkspaceOptions.Path)} is required.");

        RuleFor(x => x.HashWorkerDop)
            .GreaterThan(0)
            .WithMessage($"{nameof(WorkspaceOptions.HashWorkerDop)} must be positive.");
    }
}
