using CodebaseIndexer.Application.Services;

namespace CodebaseIndexer.Application.Tests;

public sealed class HealthServiceTests
{
    [Fact]
    public async Task GetStatus_returns_ok()
    {
        var service = new HealthService();
        var status = await service.GetStatusAsync();
        Assert.Equal("ok", status.Status);
        Assert.Equal("dotnet", status.Runtime);
    }
}
