using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Application.Services;

/// <summary>Tracks and orchestrates background index jobs.</summary>
public interface IIndexJobService
{
    /// <summary>Returns whether a job is queued or running for the collection.</summary>
    /// <param name="collection">Target collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if a job is active.</returns>
    ValueTask<bool> IsRunningAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>Gets the job snapshot for a collection.</summary>
    /// <param name="collection">Target collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with snapshot, or Failure <see cref="ErrorKind.NotFound"/> when not tracked.</returns>
    ValueTask<Result<IndexJobSnapshot>> GetJobAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>Gets snapshots of all tracked jobs.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All job snapshots.</returns>
    ValueTask<IReadOnlyList<IndexJobSnapshot>> GetAllJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts an index job for a collection.</summary>
    /// <param name="command">Start parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success with job snapshot after start (and optional wait),
    /// or Failure <see cref="ErrorKind.Conflict"/> when a job is already running and wait is false.
    /// </returns>
    Task<Result<IndexJobSnapshot>> StartAsync(IndexCodebaseCommand command, CancellationToken cancellationToken = default);

    /// <summary>Requests cancellation of a running or queued job.</summary>
    /// <param name="collection">Target collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with updated snapshot, or Failure <see cref="ErrorKind.NotFound"/> when not tracked.</returns>
    ValueTask<Result<IndexJobSnapshot>> CancelAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>Starts index jobs for all discovered collections.</summary>
    /// <param name="command">Index-all parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Snapshots of started jobs.</returns>
    Task<IReadOnlyList<IndexJobSnapshot>> IndexAllAsync(IndexAllCommand command, CancellationToken cancellationToken = default);
}
