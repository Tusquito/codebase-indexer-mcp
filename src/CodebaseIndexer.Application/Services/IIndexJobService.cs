using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Services;

/// <summary>Tracks and orchestrates background index jobs.</summary>
public interface IIndexJobService
{
    /// <summary>Returns whether a job is queued or running for the collection.</summary>
    /// <param name="collection">Target collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if a job is active.</returns>
    ValueTask<bool> IsRunningAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>Gets the job snapshot for a collection, if tracked.</summary>
    /// <param name="collection">Target collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Job snapshot, or <see langword="null"/> if not found.</returns>
    ValueTask<IndexJobSnapshot?> GetJobAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>Gets snapshots of all tracked jobs.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All job snapshots.</returns>
    ValueTask<IReadOnlyList<IndexJobSnapshot>> GetAllJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts an index job for a collection.</summary>
    /// <param name="command">Start parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Job snapshot after start (and optional wait).</returns>
    Task<IndexJobSnapshot> StartAsync(IndexCodebaseCommand command, CancellationToken cancellationToken = default);

    /// <summary>Requests cancellation of a running or queued job.</summary>
    /// <param name="collection">Target collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated job snapshot, or <see langword="null"/> if not found.</returns>
    ValueTask<IndexJobSnapshot?> CancelAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>Starts index jobs for all discovered collections.</summary>
    /// <param name="command">Index-all parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Snapshots of started jobs.</returns>
    Task<IReadOnlyList<IndexJobSnapshot>> IndexAllAsync(IndexAllCommand command, CancellationToken cancellationToken = default);
}
