using CodebaseIndexer.Infrastructure.Configuration;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Tests fail-fast validation of required host settings.</summary>
public sealed class SettingsValidateOnStartTests
{
    /// <summary>Host fails fast when required Qdrant URL is missing.</summary>
    [Test]
    public async Task Host_fails_fast_when_required_settings_missing()
    {
        await using var factory = new MissingQdrantUrlFactory();
        var ex = Assert.Throws<Exception>(() =>
        {
            using var client = factory.CreateClient();
        });
        await Assert.That(ex.ToString().Contains("Url", StringComparison.OrdinalIgnoreCase)).IsTrue();
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
