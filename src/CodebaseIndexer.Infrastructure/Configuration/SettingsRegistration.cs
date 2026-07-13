using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Configuration;

public static class SettingsRegistration
{
    public static IServiceCollection AddCodebaseIndexerSettings(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<Settings>, FluentValidateOptions<Settings>>());
        services.AddSingleton<IValidator<Settings>, SettingsValidator>();
        services.AddOptionsWithValidateOnStart<Settings>()
            .BindConfiguration(Settings.SectionName)
            .PostConfigure<IConfiguration>((options, configuration) =>
            {
                // Aspire injects service URLs via ConnectionStrings; override section defaults when present.
                if (configuration.GetConnectionString("qdrant") is { } qdrantUrl)
                {
                    options.QdrantUrl = qdrantUrl;
                }

                if (configuration.GetConnectionString("tei") is { } teiUrl)
                {
                    options.TeiUrl = teiUrl;
                }
            });

        return services;
    }
}
