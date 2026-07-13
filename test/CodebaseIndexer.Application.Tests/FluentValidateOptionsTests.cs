using CodebaseIndexer.Application.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Tests FluentValidation integration with the options pattern.</summary>
public sealed class FluentValidateOptionsTests
{
    /// <summary>Validate-on-start throws <see cref="OptionsValidationException"/> with FluentValidation message format.</summary>
    [Fact]
    public void ValidateOnStart_uses_fluent_validation_error_format()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{IndexingOptions.SectionName}:FlushEvery"] = "0",
                [$"{IndexingOptions.SectionName}:UpsertBatch"] = "500",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCodebaseIndexerIndexingOptions();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<IndexingOptions>>().Value);

        Assert.Contains("Validation failed for IndexingOptions.FlushEvery", exception.Message, StringComparison.Ordinal);
    }
}
