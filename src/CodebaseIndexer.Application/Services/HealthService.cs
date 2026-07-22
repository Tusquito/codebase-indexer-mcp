using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Services;

/// <summary>Default health service implementation.</summary>
public sealed class HealthService : IHealthService
{
    /// <inheritdoc />
    public ValueTask<HealthStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new HealthStatus(LivenessStatus.Ok, "dotnet"));
}
