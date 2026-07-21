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
                [$"{EmbeddingOptions.SectionName}:HybridSearch"] = "true",
                [$"{EmbeddingOptions.SectionName}:DenseModel"] = "",
                [$"{EmbeddingOptions.SectionName}:SparseModel"] = "s",
                [$"{EmbeddingOptions.SectionName}:DenseVectorSize"] = "768",
                [$"{EmbeddingOptions.SectionName}:CachePath"] = "/c",
                [$"{EmbeddingOptions.SectionName}:PrefetchMultiplier"] = "5",
                [$"{EmbeddingOptions.SectionName}:RrfK"] = "60",
                [$"{EmbeddingOptions.SectionName}:RerankPrefetch"] = "100",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCodebaseIndexerIndexingOptions();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value);

        Assert.Contains("Validation failed for EmbeddingOptions.DenseModel", exception.Message, StringComparison.Ordinal);
    }
}
