using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Configuration;

public sealed class SettingsValidator : IValidateOptions<Settings>
{
    public ValidateOptionsResult Validate(string? name, Settings options)
    {
        if (string.IsNullOrWhiteSpace(options.QdrantUrl))
        {
            return ValidateOptionsResult.Fail("QDRANT_URL is required.");
        }

        if (string.IsNullOrWhiteSpace(options.TeiUrl))
        {
            return ValidateOptionsResult.Fail("TEI_URL is required.");
        }

        if (options.DenseEmbedVectorSize <= 0)
        {
            return ValidateOptionsResult.Fail("DENSE_EMBED_VECTOR_SIZE must be positive.");
        }

        if (string.IsNullOrWhiteSpace(options.DenseEmbedModel))
        {
            return ValidateOptionsResult.Fail("DENSE_EMBED_MODEL is required.");
        }

        return ValidateOptionsResult.Success;
    }
}

public static class SettingsRegistration
{
    public static IServiceCollection AddCodebaseIndexerSettings(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<Settings>, SettingsValidator>();
        services.AddSingleton<IOptions<Settings>>(sp =>
        {
            var settings = Settings.FromConfiguration(sp.GetRequiredService<IConfiguration>());
            var validator = sp.GetRequiredService<IValidateOptions<Settings>>();
            var result = validator.Validate(Options.DefaultName, settings);
            if (result.Failed)
            {
                throw new OptionsValidationException(
                    Options.DefaultName,
                    typeof(Settings),
                    result.Failures.ToArray());
            }

            return Options.Create(settings);
        });

        return services;
    }
}
