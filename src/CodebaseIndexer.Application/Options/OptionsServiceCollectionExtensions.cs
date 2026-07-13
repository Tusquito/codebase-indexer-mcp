using Microsoft.Extensions.DependencyInjection;

namespace CodebaseIndexer.Application.Options;

/// <summary>Service collection helpers for validated options binding.</summary>
public static class OptionsServiceCollectionExtensions
{
    /// <summary>Binds, validates, and validates-on-start options from a configuration section.</summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configurationSection">The configuration section name.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddOptionsWithFluentValidation<TOptions>(
        this IServiceCollection services,
        string configurationSection)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .BindConfiguration(configurationSection)
            .ValidateFluentValidation()
            .ValidateOnStart();

        return services;
    }
}
