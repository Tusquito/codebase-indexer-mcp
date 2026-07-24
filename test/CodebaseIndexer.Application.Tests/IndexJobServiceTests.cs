using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Tasks;

namespace CodebaseIndexer.Application.Tests;

public sealed class IndexJobServiceTests
{
    [Test]
    public async Task StartAsync_conflict_when_already_running_without_wait()
    {
        var indexer = new StubIndexer(delay: TimeSpan.FromSeconds(30));
        var service = new IndexJobService(indexer, NullLogger<IndexJobService>.Instance);

        var first = await service.StartAsync(
            new IndexCodebaseCommand("demo", "/demo", Force: false, Wait: false, TimeoutSeconds: 5));
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(first.Value.Status is IndexJobStatus.Queued or IndexJobStatus.Running).IsTrue();

        var second = await service.StartAsync(
            new IndexCodebaseCommand("demo", "/demo", Force: false, Wait: false, TimeoutSeconds: 5));

        await Assert.That(second.IsSuccess).IsFalse();
        await Assert.That(second.Error.Kind).IsEqualTo(ErrorKind.Conflict);
        await Assert.That(second.Error.Code).IsEqualTo(IndexErrorCodes.JobAlreadyRunning);

        await service.CancelAsync("demo");
    }

    [Test]
    public async Task GetJobAsync_not_found()
    {
        var service = new IndexJobService(new StubIndexer(), NullLogger<IndexJobService>.Instance);

        var result = await service.GetJobAsync("missing");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error.Kind).IsEqualTo(ErrorKind.NotFound);
        await Assert.That(result.Error.Code).IsEqualTo(IndexErrorCodes.JobNotFound);
    }

    [Test]
    public async Task CancelAsync_not_found()
    {
        var service = new IndexJobService(new StubIndexer(), NullLogger<IndexJobService>.Instance);

        var result = await service.CancelAsync("missing");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error.Kind).IsEqualTo(ErrorKind.NotFound);
        await Assert.That(result.Error.Code).IsEqualTo(IndexErrorCodes.JobNotFound);
    }

    [Test]
    public async Task StartAsync_happy_path_returns_success()
    {
        var service = new IndexJobService(new StubIndexer(), NullLogger<IndexJobService>.Instance);

        var result = await service.StartAsync(
            new IndexCodebaseCommand("demo", "/demo", Force: false, Wait: true, TimeoutSeconds: 10));

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value.Status).IsEqualTo(IndexJobStatus.Done);
        await Assert.That(result.Value.Collection).IsEqualTo("demo");
        await Assert.That(result.Value.Errors).IsEmpty();
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