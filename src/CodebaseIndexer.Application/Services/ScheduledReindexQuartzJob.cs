using Microsoft.Extensions.Logging;
using Quartz;

namespace CodebaseIndexer.Application.Services;

/// <summary>Quartz job that invokes <see cref="IScheduledReindexRunner"/>.</summary>
public sealed class ScheduledReindexQuartzJob : IJob
{
    private readonly IScheduledReindexRunner _runner;
    private readonly ILogger<ScheduledReindexQuartzJob> _logger;

    /// <summary>Creates the Quartz job.</summary>
    public ScheduledReindexQuartzJob(
        IScheduledReindexRunner runner,
        ILogger<ScheduledReindexQuartzJob> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await _runner.RunOnceAsync(context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "scheduled_reindex_cron_failed");
            throw;
        }
    }
}
