using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Quartz job vs interval hosted service scheduling behavior.</summary>
public sealed class ScheduledReindexSchedulingTests
{
    [Fact]
    public async Task QuartzJob_Execute_invokes_runner_once()
    {
        var runner = new RecordingRunner();
        var job = new ScheduledReindexQuartzJob(runner, NullLogger<ScheduledReindexQuartzJob>.Instance);
        var context = new StubJobExecutionContext();

        await job.Execute(context);

        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public async Task IntervalHostedService_exits_immediately_when_cron_configured()
    {
        var runner = new RecordingRunner();
        var service = new ScheduledReindexIntervalHostedService(
            runner,
            MsOptions.Create(new ReindexOptions
            {
                Enabled = true,
                Cron = "0 0 3 * * ?",
                Interval = "6h",
            }),
            NullLogger<ScheduledReindexIntervalHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task IntervalHostedService_exits_immediately_when_disabled()
    {
        var runner = new RecordingRunner();
        var service = new ScheduledReindexIntervalHostedService(
            runner,
            MsOptions.Create(new ReindexOptions
            {
                Enabled = false,
                Cron = string.Empty,
                Interval = "6h",
            }),
            NullLogger<ScheduledReindexIntervalHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, runner.Calls);
    }

    private sealed class RecordingRunner : IScheduledReindexRunner
    {
        public int Calls { get; private set; }

        public Task RunOnceAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubJobExecutionContext : IJobExecutionContext
    {
        public IScheduler Scheduler => throw new NotSupportedException();
        public ITrigger Trigger => throw new NotSupportedException();
        public ICalendar? Calendar => null;
        public bool Recovering => false;
        public TriggerKey RecoveringTriggerKey => throw new NotSupportedException();
        public int RefireCount => 0;
        public JobDataMap MergedJobDataMap => new();
        public IJobDetail JobDetail => throw new NotSupportedException();
        public IJob JobInstance => throw new NotSupportedException();
        public DateTimeOffset FireTimeUtc => DateTimeOffset.UtcNow;
        public DateTimeOffset? ScheduledFireTimeUtc => DateTimeOffset.UtcNow;
        public DateTimeOffset? PreviousFireTimeUtc => null;
        public DateTimeOffset? NextFireTimeUtc => null;
        public string FireInstanceId => "test";
        public object? Result { get; set; }
        public TimeSpan JobRunTime => TimeSpan.Zero;
        public CancellationToken CancellationToken => CancellationToken.None;

        public void Put(object key, object objectValue) => throw new NotSupportedException();
        public object? Get(object key) => throw new NotSupportedException();
    }
}
