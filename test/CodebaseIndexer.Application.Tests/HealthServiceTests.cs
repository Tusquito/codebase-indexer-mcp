using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;

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
        Assert.Equal(LivenessStatus.Ok, status.Status);
        Assert.Equal("dotnet", status.Runtime);
    }
}
