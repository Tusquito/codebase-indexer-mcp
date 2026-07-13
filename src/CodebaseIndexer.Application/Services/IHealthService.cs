namespace CodebaseIndexer.Application.Services;

/// <summary>Provides server health status.</summary>
public interface IHealthService
{
    /// <summary>Returns the current health status.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health status snapshot.</returns>
    ValueTask<HealthStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
