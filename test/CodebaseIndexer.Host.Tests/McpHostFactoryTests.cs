namespace CodebaseIndexer.Host.Tests;

/// <summary>Tests MCP host startup with test configuration.</summary>
public sealed class McpHostFactoryTests
{
    /// <summary>Host starts successfully with bound configuration.</summary>
    [Test]
    public async Task Host_starts_with_bound_configuration()
    {
        await using var factory = new McpHostWebApplicationFactory();
        var client = factory.CreateClient();
        await Assert.That(client).IsNotNull();
    }
}
