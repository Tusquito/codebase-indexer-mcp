using System.ComponentModel;
using System.Text.RegularExpressions;
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

    [McpServerTool, Description("Index a project folder into the vector store.")]
    public async Task<object> IndexCodebase(
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
            return new
            {
                error = "Please specify a project folder to index.",
                hint = "Pass the project folder name as 'path'. For example: index_codebase(path='my-project').",
            };
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

            return new
            {
                message = $"Indexing already in progress for '{collection}'",
                status = existing,
            };
        }

        var snapshot = await _jobs.StartAsync(
            new IndexCodebaseCommand(collection, path, force, wait, timeout),
            cancellationToken).ConfigureAwait(false);

        if (!wait)
        {
            return new
            {
                message = $"Indexing started for '{collection}' in the background.",
                collection,
                path,
                hint = "Use index_status to check progress.",
            };
        }

        return snapshot;
    }

    [McpServerTool, Description("Check indexing job status.")]
    public async Task<object> IndexStatus(
        [Description("Optional collection name")] string? collection = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(collection))
        {
            var job = await _jobs.GetJobAsync(collection, cancellationToken).ConfigureAwait(false);
            return job is null
                ? new { error = $"No indexing job found for '{collection}'." }
                : job;
        }

        var jobs = await _jobs.GetAllJobsAsync(cancellationToken).ConfigureAwait(false);
        return jobs.Count == 0
            ? new { message = "No indexing jobs found. Use index_codebase to start one." }
            : jobs;
    }

    [McpServerTool, Description("Stop an ongoing indexing job.")]
    public async Task<object> StopIndexing(
        [Description("Collection name")] string collection,
        CancellationToken cancellationToken = default)
    {
        var cancelled = await _jobs.CancelAsync(collection, cancellationToken).ConfigureAwait(false);
        if (cancelled is null)
        {
            var existing = await _jobs.GetJobAsync(collection, cancellationToken).ConfigureAwait(false);
            return existing is null
                ? new { error = $"No indexing job found for '{collection}'." }
                : new { error = $"Job '{collection}' is not running (status: {existing.Status}).", status = existing };
        }

        return new
        {
            message = $"Cancellation requested for '{collection}'. The job will stop after the current batch.",
            collection,
            hint = "Use index_status to confirm it has stopped.",
        };
    }

    [McpServerTool, Description("Re-index all existing collections sequentially.")]
    public async Task<object> IndexAll(
        [Description("Force full re-index")] bool force = false,
        [Description("Block until all collections finish")] bool wait = true,
        [Description("Timeout per collection in seconds")] int timeout = 1800,
        CancellationToken cancellationToken = default)
    {
        var collections = await _vectorStore.ListCollectionsAsync(cancellationToken).ConfigureAwait(false);
        if (collections.Count == 0)
        {
            return new
            {
                error = "No indexed collections found.",
                hint = "Use index_codebase to index a project first, then use index_all to re-index all.",
            };
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
        return new
        {
            message = $"Indexed {succeeded}/{collections.Count} collections",
            results,
        };
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
