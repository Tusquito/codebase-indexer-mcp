using FluentValidation;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Configuration;

internal sealed class FluentValidateOptions<T> : IValidateOptions<T>
    where T : class
{
    private readonly IValidator<T> _validator;

    public FluentValidateOptions(IValidator<T> validator) => _validator = validator;

    public ValidateOptionsResult Validate(string? name, T options)
    {
        var result = _validator.Validate(options);
        return result.IsValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(result.Errors.Select(error => error.ErrorMessage));
    }
}
