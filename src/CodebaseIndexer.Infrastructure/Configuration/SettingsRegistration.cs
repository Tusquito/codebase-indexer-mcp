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
            .Configure<IConfiguration>((options, configuration) =>
            {
                var bound = SettingsBinder.Bind(configuration);
                foreach (var property in typeof(Settings).GetProperties())
                {
                    property.SetValue(options, property.GetValue(bound));
                }
            });

        return services;
    }
}
