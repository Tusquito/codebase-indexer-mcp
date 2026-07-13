using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Tests fail-fast validation of required host settings.</summary>
public sealed class SettingsValidateOnStartTests
{
    /// <summary>Host fails fast when required Qdrant URL is missing.</summary>
    [Fact]
    public void Host_fails_fast_when_required_settings_missing()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{Infrastructure.Configuration.QdrantOptions.SectionName}:Url"] = string.Empty,
                });
            });
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Url", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
