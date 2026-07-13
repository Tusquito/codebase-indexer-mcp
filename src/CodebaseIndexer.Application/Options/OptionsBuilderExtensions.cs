using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Options;

/// <summary>Extensions for wiring FluentValidation into options validation.</summary>
public static class OptionsBuilderExtensions
{
    /// <summary>Registers FluentValidation as the options validator for <typeparamref name="TOptions"/>.</summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="builder">The options builder.</param>
    /// <returns>The same options builder for chaining.</returns>
    public static OptionsBuilder<TOptions> ValidateFluentValidation<TOptions>(this OptionsBuilder<TOptions> builder)
        where TOptions : class
    {
        builder.Services.AddSingleton<IValidateOptions<TOptions>>(sp =>
            new FluentValidateOptions<TOptions>(sp, builder.Name));

        return builder;
    }
}
