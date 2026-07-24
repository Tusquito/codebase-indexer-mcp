using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using System.Threading.Tasks;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Tests for <see cref="HealthService"/>.</summary>
public sealed class HealthServiceTests
{
    /// <summary>GetStatusAsync returns ok status and dotnet runtime.</summary>
    [Test]
    public async Task GetStatus_returns_ok()
    {
        var service = new HealthService();
        var status = await service.GetStatusAsync();
        await Assert.That(status.Status).IsEqualTo(LivenessStatus.Ok);
        await Assert.That(status.Runtime).IsEqualTo("dotnet");
    }
}