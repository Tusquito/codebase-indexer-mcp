using System.ComponentModel;
using System.Text.RegularExpressions;
using CodebaseIndexer.Application.Mapping;
using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

/// <summary>MCP tools for codebase indexing operations.</summary>
[McpServerToolType]
public sealed class IndexTools
{
    private readonly IIndexJobService _jobs;
    private readonly IVectorStore _vectorStore;
    private readonly WorkspaceOptions _workspace;

    /// <summary>Initializes a new instance of the <see cref="IndexTools"/> class.</summary>
    public IndexTools(
        IIndexJobService jobs,
        IVectorStore vectorStore,
        IOptions<WorkspaceOptions> workspace)
    {
        _jobs = jobs;
        _vectorStore = vectorStore;
        _workspace = workspace.Value;
    }

    /// <summary>Indexes a project folder into the vector store.</summary>
    [McpServerTool(Name = "index_codebase"), Description("Index a project folder into the vector store.")]
    public async Task<object> IndexCodebaseAsync(
        [Description("Project folder name under WORKSPACE_ROOT")] string path = "/",
        [Description("Optional collection override")] string? collection = null,
        [Description("Force full re-index even if file SHA unchanged")] bool force = false,
        [Description("Block until indexing completes")] bool wait = true,
        [Description("Timeout in seconds when wait=true")] int timeout = 1800,
        CancellationToken cancellationToken = default)
    {
        path = NormalizePath(path);
        if (path == "/")
        {
            return McpErrorMapper.FromError(new Error(
                ErrorKind.Validation,
                McpErrorCodes.PathRequired,
                "Please specify a project folder to index.",
                new Dictionary<string, string>
                {
                    ["hint"] = "Pass the project folder name as 'path'. For example: index_codebase(path='my-project').",
                }));
        }

        collection ??= DeriveCollectionName(_workspace.Path, path);
        if (await _jobs.IsRunningAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            var existingResult = await _jobs.GetJobAsync(collection, cancellationToken).ConfigureAwait(false);
            if (wait && existingResult.IsSuccess)
            {
                return MapStartResult(
                    await _jobs.StartAsync(
                        new IndexCodebaseCommand(collection, path, force, wait, timeout),
                        cancellationToken).ConfigureAwait(false),
                    collection,
                    path,
                    wait);
            }

            return existingResult.Match(
                onSuccess: snapshot => ConflictAlreadyRunning(collection, path, snapshot),
                onFailure: _ => ConflictAlreadyRunning(
                    collection,
                    path,
                    new IndexJobSnapshot(
                        collection,
                        path,
                        IndexJobStatus.Running,
                        0,
                        0,
                        0,
                        0,
                        0,
                        Array.Empty<Error>())));
        }

        return MapStartResult(
            await _jobs.StartAsync(
                new IndexCodebaseCommand(collection, path, force, wait, timeout),
                cancellationToken).ConfigureAwait(false),
            collection,
            path,
            wait);
    }

    /// <summary>Checks indexing job status for one or all collections.</summary>
    [McpServerTool(Name = "index_status"), Description("Check indexing job status.")]
    public async Task<object> IndexStatusAsync(
        [Description("Optional collection name")] string? collection = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(collection))
        {
            var jobResult = await _jobs.GetJobAsync(collection, cancellationToken).ConfigureAwait(false);
            return jobResult.Match(
                onSuccess: job => (object)job,
                onFailure: McpErrorMapper.FromError);
        }

        var jobs = await _jobs.GetAllJobsAsync(cancellationToken).ConfigureAwait(false);
        return jobs.Count == 0
            ? new IndexStatusEmptyResponse(Message: "No indexing jobs found. Use index_codebase to start one.")
            : jobs;
    }

    /// <summary>Requests cancellation of an ongoing indexing job.</summary>
    [McpServerTool(Name = "stop_indexing"), Description("Stop an ongoing indexing job.")]
    public async Task<object> StopIndexingAsync(
        [Description("Collection name")] string collection,
        CancellationToken cancellationToken = default)
    {
        var cancelResult = await _jobs.CancelAsync(collection, cancellationToken).ConfigureAwait(false);
        if (!cancelResult.IsSuccess)
        {
            return McpErrorMapper.FromError(cancelResult.Error);
        }

        var snapshot = cancelResult.Value;
        if (snapshot.Status is not (IndexJobStatus.Queued or IndexJobStatus.Running))
        {
            return McpErrorMapper.FromError(new Error(
                ErrorKind.Conflict,
                McpErrorCodes.JobNotRunning,
                $"Job '{collection}' is not running (status: {snapshot.Status}).",
                SnapshotMetadata(snapshot)));
        }

        return new IndexCancelledResponse(
            Message: $"Cancellation requested for '{collection}'. The job will stop after the current batch.",
            Collection: collection,
            Hint: "Use index_status to confirm it has stopped.");
    }

    /// <summary>Re-indexes all existing collections sequentially.</summary>
    [McpServerTool(Name = "index_all"), Description("Re-index all existing collections sequentially.")]
    public async Task<object> IndexAllAsync(
        [Description("Force full re-index")] bool force = false,
        [Description("Block until all collections finish")] bool wait = true,
        [Description("Timeout per collection in seconds")] int timeout = 1800,
        CancellationToken cancellationToken = default)
    {
        var collectionsResult = await _vectorStore.ListCollectionsAsync(cancellationToken).ConfigureAwait(false);
        if (!collectionsResult.IsSuccess)
        {
            return McpErrorMapper.FromError(collectionsResult.Error);
        }

        var collections = collectionsResult.Value;
        if (collections.Count == 0)
        {
            return McpErrorMapper.FromError(new Error(
                ErrorKind.NotFound,
                McpErrorCodes.CollectionsEmpty,
                "No indexed collections found.",
                new Dictionary<string, string>
                {
                    ["hint"] = "Use index_codebase to index a project first, then use index_all to re-index all.",
                }));
        }

        var results = new List<IndexJobSnapshot>();
        foreach (var name in collections)
        {
            var path = $"/{name}";
            if (await _jobs.IsRunningAsync(name, cancellationToken).ConfigureAwait(false))
            {
                var existing = await _jobs.GetJobAsync(name, cancellationToken).ConfigureAwait(false);
                if (existing.IsSuccess)
                {
                    results.Add(existing.Value);
                }

                continue;
            }

            var startResult = await _jobs.StartAsync(
                new IndexCodebaseCommand(name, path, force, wait, timeout),
                cancellationToken).ConfigureAwait(false);
            if (startResult.IsSuccess)
            {
                results.Add(startResult.Value);
            }
        }

        var succeeded = results.Count(r => r.Status == IndexJobStatus.Done);
        return new IndexAllCompletedResponse(
            Message: $"Indexed {succeeded}/{collections.Count} collections",
            Results: results);
    }

    private static object MapStartResult(
        Result<IndexJobSnapshot> startResult,
        string collection,
        string path,
        bool wait) =>
        startResult.Match(
            onSuccess: snapshot =>
            {
                if (!wait)
                {
                    return (object)new IndexStartedResponse(
                        Message: $"Indexing started for '{collection}' in the background.",
                        Collection: collection,
                        Path: path,
                        Hint: "Use index_status to check progress.");
                }

                return snapshot;
            },
            onFailure: error => error.Kind == ErrorKind.Conflict
                ? ConflictAlreadyRunning(
                    collection,
                    path,
                    new IndexJobSnapshot(
                        collection,
                        path,
                        IndexJobStatus.Running,
                        0,
                        0,
                        0,
                        0,
                        0,
                        Array.Empty<Error>(),
                        error.Message))
                : McpErrorMapper.FromError(error));

    private static object ConflictAlreadyRunning(string collection, string path, IndexJobSnapshot status) =>
        McpErrorMapper.FromError(new Error(
            ErrorKind.Conflict,
            IndexErrorCodes.JobAlreadyRunning,
            $"Indexing already in progress for '{collection}'",
            SnapshotMetadata(status)));

    private static IReadOnlyDictionary<string, string> SnapshotMetadata(IndexJobSnapshot snapshot) =>
        new Dictionary<string, string>
        {
            ["collection"] = snapshot.Collection,
            ["path"] = snapshot.Path,
            ["status"] = snapshot.Status.ToString(),
            ["total_files"] = snapshot.TotalFiles.ToString(),
            ["indexed_files"] = snapshot.IndexedFiles.ToString(),
            ["skipped_files"] = snapshot.SkippedFiles.ToString(),
            ["total_chunks"] = snapshot.TotalChunks.ToString(),
            ["elapsed_seconds"] = snapshot.ElapsedSeconds.ToString(),
        };

    internal static string NormalizePath(string rawPath)
    {
        var p = rawPath.Trim().Replace('\\', '/');
        p = Regex.Replace(p, @"^[A-Za-z]:/", "/");
        p = Regex.Replace(p, "^/workspace/", "/");
        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "/" : "/" + parts[^1];
    }

    internal static string DeriveCollectionName(string workspacePath, string subPath)
    {
        var clean = subPath.Trim('/');
        if (string.IsNullOrEmpty(clean))
        {
            return Path.GetFileName(Path.GetFullPath(workspacePath));
        }

        return clean.Split('/')[0];
    }
}
