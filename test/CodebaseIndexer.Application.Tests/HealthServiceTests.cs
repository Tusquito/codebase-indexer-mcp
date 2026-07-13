using CodebaseIndexer.Application.Services;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Tests for <see cref="HealthService"/>.</summary>
public sealed class HealthServiceTests
{
    /// <summary>GetStatusAsync returns ok status and dotnet runtime.</summary>
    [Fact]
    public async Task GetStatus_returns_ok()
    {
        var service = new HealthService();
        var status = await service.GetStatusAsync();
        Assert.Equal("ok", status.Status);
        Assert.Equal("dotnet", status.Runtime);
    }
}
