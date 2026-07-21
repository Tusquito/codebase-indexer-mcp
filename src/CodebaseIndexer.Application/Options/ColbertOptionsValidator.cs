using FluentValidation;

namespace CodebaseIndexer.Application.Options;

/// <summary>FluentValidation rules for <see cref="ColbertOptions"/>.</summary>
public sealed class ColbertOptionsValidator : AbstractValidator<ColbertOptions>
{
    /// <summary>Creates validation rules for ColBERT settings.</summary>
    public ColbertOptionsValidator()
    {
        RuleFor(x => x.EmbedModel)
            .NotEmpty()
            .WithMessage($"{nameof(ColbertOptions.EmbedModel)} is required.");

        RuleFor(x => x.EmbedBackend)
            .Must(b => string.IsNullOrWhiteSpace(b)
                || string.Equals(b, "onnx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(b, "remote", StringComparison.OrdinalIgnoreCase))
            .WithMessage($"{nameof(ColbertOptions.EmbedBackend)} must be 'onnx', 'remote', or empty.");

        RuleFor(x => x.TimeoutSeconds)
            .GreaterThan(0)
            .WithMessage($"{nameof(ColbertOptions.TimeoutSeconds)} must be positive.");

        RuleFor(x => x.EmbedBatchSize)
            .GreaterThan(0)
            .WithMessage($"{nameof(ColbertOptions.EmbedBatchSize)} must be positive.");

        RuleFor(x => x.GpuMemLimitBytes)
            .GreaterThanOrEqualTo(0)
            .WithMessage($"{nameof(ColbertOptions.GpuMemLimitBytes)} must be >= 0 (0 omits ORT gpu_mem_limit).");
    }
}
