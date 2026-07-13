namespace CodebaseIndexer.Application.Services;

public sealed record HealthStatus(string Status, string Runtime);

public interface IHealthService
{
    ValueTask<HealthStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed class HealthService : IHealthService
{
    public ValueTask<HealthStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new HealthStatus("ok", "dotnet"));
}
