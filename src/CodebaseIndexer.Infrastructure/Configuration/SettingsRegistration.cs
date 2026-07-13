using CodebaseIndexer.Application.Options;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Configuration;

/// <summary>Registers infrastructure configuration options and validators.</summary>
public static class SettingsRegistration
{
    /// <summary>Binds Qdrant, TEI, chunking, and known-embed-model options.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddCodebaseIndexerSettings(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<QdrantOptionsValidator>();

        services.AddOptionsWithFluentValidation<QdrantOptions>(QdrantOptions.SectionName);
        services.AddOptionsWithFluentValidation<TeiOptions>(TeiOptions.SectionName);
        services.AddOptionsWithFluentValidation<ChunkingOptions>(ChunkingOptions.SectionName);

        services.AddSingleton<IOptions<KnownEmbedModelsOptions>>(sp =>
            Options.Create(KnownEmbedModelsFactory.Create(sp.GetRequiredService<IConfiguration>())));

        return services;
    }
}
