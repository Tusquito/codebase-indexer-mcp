using CodebaseIndexer.Application.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Services;

/// <summary>Interval-based scheduled reindex when <see cref="ReindexOptions.Cron"/> is empty.</summary>
public sealed class ScheduledReindexIntervalHostedService : BackgroundService
{
    private readonly IScheduledReindexRunner _runner;
    private readonly ReindexOptions _options;
    private readonly ILogger<ScheduledReindexIntervalHostedService> _logger;

    /// <summary>Creates the interval hosted service.</summary>
    public ScheduledReindexIntervalHostedService(
        IScheduledReindexRunner runner,
        IOptions<ReindexOptions> options,
        ILogger<ScheduledReindexIntervalHostedService> logger)
    {
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !string.IsNullOrWhiteSpace(_options.Cron))
        {
            return;
        }

        if (!TryParseInterval(_options.Interval, out var period))
        {
            _logger.LogWarning("scheduled_reindex_invalid_interval value={Interval}", _options.Interval);
            return;
        }

        _logger.LogInformation("scheduled_reindex_interval_start period={Period}", period);
        using var timer = new PeriodicTimer(period);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await _runner.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "scheduled_reindex_interval_failed");
            }
        }
    }

    /// <summary>Parses interval strings like <c>6h</c>, <c>30m</c>, or TimeSpan text.</summary>
    internal static bool TryParseInterval(string raw, out TimeSpan period)
    {
        period = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = raw.Trim();
        if (TimeSpan.TryParse(raw, out period) && period > TimeSpan.Zero)
        {
            return true;
        }

        if (raw.Length < 2)
        {
            return false;
        }

        var unit = raw[^1];
        if (!double.TryParse(raw[..^1], out var value) || value <= 0)
        {
            return false;
        }

        period = unit switch
        {
            's' or 'S' => TimeSpan.FromSeconds(value),
            'm' or 'M' => TimeSpan.FromMinutes(value),
            'h' or 'H' => TimeSpan.FromHours(value),
            'd' or 'D' => TimeSpan.FromDays(value),
            _ => TimeSpan.Zero,
        };
        return period > TimeSpan.Zero;
    }
}
