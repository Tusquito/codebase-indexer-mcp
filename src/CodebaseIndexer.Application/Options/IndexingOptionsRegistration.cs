using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseIndexer.Application.Options;

/// <summary>Registers validated indexing-related options.</summary>
public static class IndexingOptionsRegistration
{
    /// <summary>Registers FluentValidation validators and binds indexing options from configuration.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddCodebaseIndexerIndexingOptions(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<EmbeddingOptionsValidator>();

        services.AddOptionsWithFluentValidation<EmbeddingOptions>(EmbeddingOptions.SectionName);
        services.AddOptionsWithFluentValidation<WorkspaceOptions>(WorkspaceOptions.SectionName);
        services.AddOptionsWithFluentValidation<IndexingOptions>(IndexingOptions.SectionName);
        services.AddOptionsWithFluentValidation<DiscoveryOptions>(DiscoveryOptions.SectionName);

        return services;
    }
}
