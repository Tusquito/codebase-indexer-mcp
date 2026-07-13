using System.Collections.Concurrent;
using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Models;
using Microsoft.Extensions.Logging;

namespace CodebaseIndexer.Application.Services;

/// <summary>In-memory tracker and runner for background index jobs.</summary>
public sealed class IndexJobService : IIndexJobService
{
    private const int MaxJobs = 100;
    private readonly ConcurrentDictionary<string, IndexJobState> _jobs = new(StringComparer.Ordinal);
    private readonly IIndexCodebaseService _indexer;
    private readonly ILogger<IndexJobService> _logger;

    /// <summary>Creates the index job service.</summary>
    /// <param name="indexer">Codebase indexing pipeline.</param>
    /// <param name="logger">Logger instance.</param>
    public IndexJobService(IIndexCodebaseService indexer, ILogger<IndexJobService> logger)
    {
        _indexer = indexer;
        _logger = logger;
    }

    /// <inheritdoc />
    public ValueTask<bool> IsRunningAsync(string collection, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(collection, out var job))
        {
            return ValueTask.FromResult(job.Status is IndexJobStatus.Queued or IndexJobStatus.Running);
        }

        return ValueTask.FromResult(false);
    }

    /// <inheritdoc />
    public ValueTask<IndexJobSnapshot?> GetJobAsync(string collection, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_jobs.TryGetValue(collection, out var job) ? job.ToSnapshot() : null);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<IndexJobSnapshot>> GetAllJobsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<IndexJobSnapshot>>(_jobs.Values.Select(j => j.ToSnapshot()).ToArray());

    /// <inheritdoc />
    public async Task<IndexJobSnapshot> StartAsync(IndexCodebaseCommand command, CancellationToken cancellationToken = default)
    {
        if (await IsRunningAsync(command.Collection, cancellationToken).ConfigureAwait(false))
        {
            var existing = await GetJobAsync(command.Collection, cancellationToken).ConfigureAwait(false);
            if (command.Wait && existing is not null && _jobs.TryGetValue(command.Collection, out var running))
            {
                await WaitForJobAsync(running, command.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
                return running.ToSnapshot();
            }

            return existing ?? throw new InvalidOperationException($"Job '{command.Collection}' already running.");
        }

        var job = new IndexJobState
        {
            Collection = command.Collection,
            Path = command.Path,
            Force = command.Force,
            StartedAt = DateTimeOffset.UtcNow,
        };
        EvictIfNeeded();
        _jobs[command.Collection] = job;

        job.RunTask = RunJobAsync(job);
        if (!command.Wait)
        {
            return job.ToSnapshot();
        }

        await WaitForJobAsync(job, command.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
        return job.ToSnapshot();
    }

    /// <inheritdoc />
    public ValueTask<IndexJobSnapshot?> CancelAsync(string collection, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(collection, out var job))
        {
            return ValueTask.FromResult<IndexJobSnapshot?>(null);
        }

        if (job.Status is not (IndexJobStatus.Queued or IndexJobStatus.Running))
        {
            return ValueTask.FromResult<IndexJobSnapshot?>(job.ToSnapshot());
        }

        job.Cancellation.Cancel();
        return ValueTask.FromResult<IndexJobSnapshot?>(job.ToSnapshot());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexJobSnapshot>> IndexAllAsync(IndexAllCommand command, CancellationToken cancellationToken = default)
    {
        // Index-all discovers collections via job tracker + vector store list in tools layer.
        return Array.Empty<IndexJobSnapshot>();
    }

    internal async Task RunJobAsync(IndexJobState job)
    {
        job.Status = IndexJobStatus.Running;
        try
        {
            var result = await _indexer.RunAsync(
                job.Collection,
                job.Path,
                job.Force,
                job.Cancellation.Token).ConfigureAwait(false);
            job.Result = result;
            job.Status = IndexJobStatus.Done;
        }
        catch (OperationCanceledException)
        {
            job.Status = IndexJobStatus.Cancelled;
            job.ErrorMessage = "Indexing cancelled.";
        }
        catch (IndexCancelledException ex)
        {
            job.Status = IndexJobStatus.Cancelled;
            job.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            job.Status = IndexJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            _logger.LogError(ex, "index_job_failed collection={Collection}", job.Collection);
        }
        finally
        {
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.Completed.TrySetResult();
        }
    }

    private static async Task WaitForJobAsync(IndexJobState job, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        await job.Completed.Task.WaitAsync(linked.Token).ConfigureAwait(false);
    }

    private void EvictIfNeeded()
    {
        while (_jobs.Count > MaxJobs)
        {
            var terminal = _jobs.FirstOrDefault(kv => kv.Value.Status is IndexJobStatus.Done or IndexJobStatus.Failed or IndexJobStatus.Cancelled);
            if (terminal.Key is null)
            {
                break;
            }

            _jobs.TryRemove(terminal.Key, out _);
        }
    }
}
