using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodebaseIndexer.Application.Tests;

public sealed class IndexJobServiceTests
{
    [Fact]
    public async Task StartAsync_conflict_when_already_running_without_wait()
    {
        var indexer = new StubIndexer(delay: TimeSpan.FromSeconds(30));
        var service = new IndexJobService(indexer, NullLogger<IndexJobService>.Instance);

        var first = await service.StartAsync(
            new IndexCodebaseCommand("demo", "/demo", Force: false, Wait: false, TimeoutSeconds: 5));
        Assert.True(first.IsSuccess);
        Assert.True(first.Value.Status is IndexJobStatus.Queued or IndexJobStatus.Running);

        var second = await service.StartAsync(
            new IndexCodebaseCommand("demo", "/demo", Force: false, Wait: false, TimeoutSeconds: 5));

        Assert.False(second.IsSuccess);
        Assert.Equal(ErrorKind.Conflict, second.Error.Kind);
        Assert.Equal(IndexErrorCodes.JobAlreadyRunning, second.Error.Code);

        await service.CancelAsync("demo");
    }

    [Fact]
    public async Task GetJobAsync_not_found()
    {
        var service = new IndexJobService(new StubIndexer(), NullLogger<IndexJobService>.Instance);

        var result = await service.GetJobAsync("missing");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal(IndexErrorCodes.JobNotFound, result.Error.Code);
    }

    [Fact]
    public async Task CancelAsync_not_found()
    {
        var service = new IndexJobService(new StubIndexer(), NullLogger<IndexJobService>.Instance);

        var result = await service.CancelAsync("missing");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal(IndexErrorCodes.JobNotFound, result.Error.Code);
    }

    [Fact]
    public async Task StartAsync_happy_path_returns_success()
    {
        var service = new IndexJobService(new StubIndexer(), NullLogger<IndexJobService>.Instance);

        var result = await service.StartAsync(
            new IndexCodebaseCommand("demo", "/demo", Force: false, Wait: true, TimeoutSeconds: 10));

        Assert.True(result.IsSuccess);
        Assert.Equal(IndexJobStatus.Done, result.Value.Status);
        Assert.Equal("demo", result.Value.Collection);
        Assert.Empty(result.Value.Errors);
    }

    private sealed class StubIndexer(TimeSpan? delay = null) : IIndexCodebaseService
    {
        private readonly TimeSpan _delay = delay ?? TimeSpan.Zero;

        public async Task<Result<PipelineResult>> RunAsync(
            string collection,
            string subPath,
            bool force,
            CancellationToken cancellationToken)
        {
            _ = collection;
            _ = subPath;
            _ = force;
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            }

            return Result<PipelineResult>.Success(new PipelineResult());
        }
    }
}
