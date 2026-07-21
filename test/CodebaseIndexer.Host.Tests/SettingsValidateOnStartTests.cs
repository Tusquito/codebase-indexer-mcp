using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Tests fail-fast validation of required host settings.</summary>
public sealed class SettingsValidateOnStartTests
{
    /// <summary>Host fails fast when required Qdrant URL is missing.</summary>
    [Fact]
    public void Host_fails_fast_when_required_settings_missing()
    {
        using var factory = new MissingQdrantUrlFactory();
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var client = factory.CreateClient();
        });
        Assert.Contains("Url", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MissingQdrantUrlFactory : McpHostWebApplicationFactory
    {
        public MissingQdrantUrlFactory()
            : base(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{QdrantOptions.SectionName}:Url"] = string.Empty,
            })
        {
        }
    }
}
