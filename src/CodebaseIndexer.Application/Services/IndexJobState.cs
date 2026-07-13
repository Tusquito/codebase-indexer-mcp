using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Services;

internal sealed class IndexJobState
{
    public required string Collection { get; init; }
    public required string Path { get; init; }
    public bool Force { get; init; }
    public IndexJobStatus Status { get; set; } = IndexJobStatus.Queued;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public PipelineResult? Result { get; set; }
    public CancellationTokenSource Cancellation { get; } = new();
    public Task? RunTask { get; set; }
    public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public double ElapsedSeconds
    {
        get
        {
            if (StartedAt == default)
            {
                return 0;
            }

            var end = FinishedAt ?? DateTimeOffset.UtcNow;
            return Math.Round((end - StartedAt).TotalSeconds, 2);
        }
    }

    public IndexJobSnapshot ToSnapshot() => new(
        Collection,
        Path,
        Status,
        ElapsedSeconds,
        Result?.TotalFiles ?? 0,
        Result?.IndexedFiles ?? 0,
        Result?.SkippedFiles ?? 0,
        Result?.TotalChunks ?? 0,
        Result?.Errors ?? Array.Empty<string>(),
        ErrorMessage);
}
