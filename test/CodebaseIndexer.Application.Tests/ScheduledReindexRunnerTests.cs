using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Application.Tests;

/// <summary>
/// In-process scheduled reindex (GitPull / IndexJobService — no MCP HTTP).
/// </summary>
public sealed class ScheduledReindexRunnerTests
{
    [Fact]
    public async Task RunOnce_no_collections_skips_jobs()
    {
        var jobs = new FakeJobs();
        var store = new FakeStore([]);
        var runner = CreateRunner(jobs, store, gitPull: false, workspace: Path.GetTempPath());

        await runner.RunOnceAsync();

        Assert.Empty(jobs.Starts);
    }

    [Fact]
    public async Task RunOnce_git_pull_false_starts_index_job_without_mcp_http()
    {
        var root = Directory.CreateTempSubdirectory("reindex-ws-");
        try
        {
            var collection = "demo-coll";
            Directory.CreateDirectory(Path.Combine(root.FullName, collection));
            var jobs = new FakeJobs();
            var store = new FakeStore([collection]);
            var runner = CreateRunner(jobs, store, gitPull: false, workspace: root.FullName);

            await runner.RunOnceAsync();

            Assert.Single(jobs.Starts);
            var cmd = jobs.Starts[0];
            Assert.Equal(collection, cmd.Collection);
            Assert.Equal("/" + collection, cmd.Path);
            Assert.False(cmd.Force);
            Assert.True(cmd.Wait);
            Assert.Equal(1800, cmd.TimeoutSeconds);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunOnce_missing_collection_path_skips_without_start()
    {
        var root = Directory.CreateTempSubdirectory("reindex-missing-");
        try
        {
            var jobs = new FakeJobs();
            var store = new FakeStore(["absent"]);
            var runner = CreateRunner(jobs, store, gitPull: false, workspace: root.FullName);

            await runner.RunOnceAsync();

            Assert.Empty(jobs.Starts);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunOnce_git_pull_true_non_git_path_skips()
    {
        var root = Directory.CreateTempSubdirectory("reindex-nongit-");
        try
        {
            var collection = "plain-dir";
            Directory.CreateDirectory(Path.Combine(root.FullName, collection));
            var jobs = new FakeJobs();
            var store = new FakeStore([collection]);
            var runner = CreateRunner(jobs, store, gitPull: true, workspace: root.FullName);

            await runner.RunOnceAsync();

            Assert.Empty(jobs.Starts);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunOnce_already_running_skips_start()
    {
        var root = Directory.CreateTempSubdirectory("reindex-running-");
        try
        {
            var collection = "busy";
            Directory.CreateDirectory(Path.Combine(root.FullName, collection));
            var jobs = new FakeJobs { Running = [collection] };
            var store = new FakeStore([collection]);
            var runner = CreateRunner(jobs, store, gitPull: false, workspace: root.FullName);

            await runner.RunOnceAsync();

            Assert.Empty(jobs.Starts);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunOnce_start_failure_increments_errors_without_throw()
    {
        var root = Directory.CreateTempSubdirectory("reindex-fail-");
        try
        {
            var collection = "fail-coll";
            Directory.CreateDirectory(Path.Combine(root.FullName, collection));
            var jobs = new FakeJobs
            {
                StartFailure = new Error(ErrorKind.Conflict, IndexErrorCodes.JobAlreadyRunning, "busy"),
            };
            var store = new FakeStore([collection]);
            var runner = CreateRunner(jobs, store, gitPull: false, workspace: root.FullName);

            await runner.RunOnceAsync();

            Assert.Single(jobs.Starts);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static ScheduledReindexRunner CreateRunner(
        FakeJobs jobs,
        FakeStore store,
        bool gitPull,
        string workspace) =>
        new(
            jobs,
            store,
            MsOptions.Create(new ReindexOptions
            {
                Enabled = true,
                Cron = string.Empty,
                Interval = "6h",
                GitPull = gitPull,
                WorkspacePath = workspace,
                IndexTimeoutSeconds = 1800,
                GitTimeoutSeconds = 120,
            }),
            MsOptions.Create(new WorkspaceOptions { Path = workspace }),
            NullLogger<ScheduledReindexRunner>.Instance);

    private sealed class FakeJobs : IIndexJobService
    {
        public List<IndexCodebaseCommand> Starts { get; } = [];
        public HashSet<string> Running { get; init; } = [];
        public Error? StartFailure { get; init; }

        public ValueTask<bool> IsRunningAsync(string collection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Running.Contains(collection));

        public ValueTask<Result<IndexJobSnapshot>> GetJobAsync(string collection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<IndexJobSnapshot>.Failure(new Error(
                ErrorKind.NotFound,
                IndexErrorCodes.JobNotFound,
                $"No indexing job found for '{collection}'.")));

        public ValueTask<IReadOnlyList<IndexJobSnapshot>> GetAllJobsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<IndexJobSnapshot>>([]);

        public Task<Result<IndexJobSnapshot>> StartAsync(IndexCodebaseCommand command, CancellationToken cancellationToken = default)
        {
            Starts.Add(command);
            if (StartFailure is not null)
            {
                return Task.FromResult(Result<IndexJobSnapshot>.Failure(StartFailure));
            }

            return Task.FromResult(Result<IndexJobSnapshot>.Success(new IndexJobSnapshot(
                command.Collection,
                command.Path,
                IndexJobStatus.Done,
                0,
                0,
                0,
                0,
                0,
                [])));
        }

        public ValueTask<Result<IndexJobSnapshot>> CancelAsync(string collection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<IndexJobSnapshot>.Failure(new Error(
                ErrorKind.NotFound,
                IndexErrorCodes.JobNotFound,
                $"No indexing job found for '{collection}'.")));

        public Task<IReadOnlyList<IndexJobSnapshot>> IndexAllAsync(
            IndexAllCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IndexJobSnapshot>>([]);
    }

    private sealed class FakeStore : NoOpVectorStore
    {
        private readonly IReadOnlyList<string> _collections;

        public FakeStore(IReadOnlyList<string> collections) => _collections = collections;

        public override Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_collections);
    }
}
