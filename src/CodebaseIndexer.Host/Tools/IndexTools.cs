using System.ComponentModel;
using System.Text.RegularExpressions;
using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace CodebaseIndexer.Host.Tools;

[McpServerToolType]
public sealed class IndexTools
{
    private readonly IIndexJobService _jobs;
    private readonly IVectorStore _vectorStore;
    private readonly Settings _settings;

    public IndexTools(
        IIndexJobService jobs,
        IVectorStore vectorStore,
        IOptions<Settings> settings)
    {
        _jobs = jobs;
        _vectorStore = vectorStore;
        _settings = settings.Value;
    }

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
            return new IndexPathRequiredResponse(
                Error: "Please specify a project folder to index.",
                Hint: "Pass the project folder name as 'path'. For example: index_codebase(path='my-project').");
        }

        collection ??= DeriveCollectionName(_settings.WorkspacePath, path);
        if (await _jobs.IsRunningAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            var existing = await _jobs.GetJobAsync(collection, cancellationToken).ConfigureAwait(false);
            if (wait && existing is not null)
            {
                return await _jobs.StartAsync(
                    new IndexCodebaseCommand(collection, path, force, wait, timeout),
                    cancellationToken).ConfigureAwait(false);
            }

            return new IndexAlreadyRunningResponse(
                Message: $"Indexing already in progress for '{collection}'",
                Status: existing!);
        }

        var snapshot = await _jobs.StartAsync(
            new IndexCodebaseCommand(collection, path, force, wait, timeout),
            cancellationToken).ConfigureAwait(false);

        if (!wait)
        {
            return new IndexStartedResponse(
                Message: $"Indexing started for '{collection}' in the background.",
                Collection: collection,
                Path: path,
                Hint: "Use index_status to check progress.");
        }

        return snapshot;
    }

    [McpServerTool(Name = "index_status"), Description("Check indexing job status.")]
    public async Task<object> IndexStatusAsync(
        [Description("Optional collection name")] string? collection = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(collection))
        {
            var job = await _jobs.GetJobAsync(collection, cancellationToken).ConfigureAwait(false);
            return job is null
                ? new IndexJobNotFoundResponse(Error: $"No indexing job found for '{collection}'.")
                : job;
        }

        var jobs = await _jobs.GetAllJobsAsync(cancellationToken).ConfigureAwait(false);
        return jobs.Count == 0
            ? new IndexStatusEmptyResponse(Message: "No indexing jobs found. Use index_codebase to start one.")
            : jobs;
    }

    [McpServerTool(Name = "stop_indexing"), Description("Stop an ongoing indexing job.")]
    public async Task<object> StopIndexingAsync(
        [Description("Collection name")] string collection,
        CancellationToken cancellationToken = default)
    {
        var cancelled = await _jobs.CancelAsync(collection, cancellationToken).ConfigureAwait(false);
        if (cancelled is null)
        {
            var existing = await _jobs.GetJobAsync(collection, cancellationToken).ConfigureAwait(false);
            return existing is null
                ? new IndexJobNotFoundResponse(Error: $"No indexing job found for '{collection}'.")
                : new IndexJobNotRunningResponse(
                    Error: $"Job '{collection}' is not running (status: {existing.Status}).",
                    Status: existing);
        }

        return new IndexCancelledResponse(
            Message: $"Cancellation requested for '{collection}'. The job will stop after the current batch.",
            Collection: collection,
            Hint: "Use index_status to confirm it has stopped.");
    }

    [McpServerTool(Name = "index_all"), Description("Re-index all existing collections sequentially.")]
    public async Task<object> IndexAllAsync(
        [Description("Force full re-index")] bool force = false,
        [Description("Block until all collections finish")] bool wait = true,
        [Description("Timeout per collection in seconds")] int timeout = 1800,
        CancellationToken cancellationToken = default)
    {
        var collections = await _vectorStore.ListCollectionsAsync(cancellationToken).ConfigureAwait(false);
        if (collections.Count == 0)
        {
            return new IndexAllEmptyResponse(
                Error: "No indexed collections found.",
                Hint: "Use index_codebase to index a project first, then use index_all to re-index all.");
        }

        var results = new List<IndexJobSnapshot>();
        foreach (var name in collections)
        {
            var path = $"/{name}";
            if (await _jobs.IsRunningAsync(name, cancellationToken).ConfigureAwait(false))
            {
                var existing = await _jobs.GetJobAsync(name, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    results.Add(existing);
                }

                continue;
            }

            var snapshot = await _jobs.StartAsync(
                new IndexCodebaseCommand(name, path, force, wait, timeout),
                cancellationToken).ConfigureAwait(false);
            results.Add(snapshot);
        }

        var succeeded = results.Count(r => r.Status == IndexJobStatus.Done);
        return new IndexAllCompletedResponse(
            Message: $"Indexed {succeeded}/{collections.Count} collections",
            Results: results);
    }

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
