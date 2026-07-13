namespace CodebaseIndexer.Host.Tests;

/// <summary>Tests MCP host startup with test configuration.</summary>
public sealed class McpHostFactoryTests
{
    /// <summary>Host starts successfully with bound configuration.</summary>
    [Fact]
    public void Host_starts_with_bound_configuration()
    {
        using var factory = new McpHostWebApplicationFactory();
        var client = factory.CreateClient();
        Assert.NotNull(client);
    }
}
