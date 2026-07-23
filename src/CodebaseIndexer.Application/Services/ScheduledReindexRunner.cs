using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Services;

/// <summary>Git-pull + <see cref="IIndexJobService"/> reindex (replaces cron sidecar).</summary>
public sealed class ScheduledReindexRunner : IScheduledReindexRunner
{
    private readonly IIndexJobService _jobs;
    private readonly IVectorStore _store;
    private readonly ReindexOptions _reindex;
    private readonly WorkspaceOptions _workspace;
    private readonly ILogger<ScheduledReindexRunner> _logger;

    /// <summary>Creates the scheduled reindex runner.</summary>
    public ScheduledReindexRunner(
        IIndexJobService jobs,
        IVectorStore store,
        IOptions<ReindexOptions> reindex,
        IOptions<WorkspaceOptions> workspace,
        ILogger<ScheduledReindexRunner> logger)
    {
        _jobs = jobs;
        _store = store;
        _reindex = reindex.Value;
        _workspace = workspace.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var workspaceRoot = string.IsNullOrWhiteSpace(_reindex.WorkspacePath)
            ? _workspace.Path
            : _reindex.WorkspacePath;

        _logger.LogInformation(
            "scheduled_reindex_start workspace={Workspace} git_pull={GitPull}",
            workspaceRoot,
            _reindex.GitPull);

        var collections = await _store.ListCollectionsAsync(cancellationToken).ConfigureAwait(false);
        if (collections.Count == 0)
        {
            _logger.LogInformation("scheduled_reindex_skip reason=no_collections");
            return;
        }

        var reindexed = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var name in collections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var repoPath = Path.Combine(workspaceRoot, name);
            _logger.LogInformation("scheduled_reindex_collection collection={Collection}", name);

            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("scheduled_reindex_missing_path collection={Collection} path={Path}", name, repoPath);
                skipped++;
                continue;
            }

            if (_reindex.GitPull)
            {
                var (ok, changed, message) = TryGitPull(repoPath);
                if (!ok)
                {
                    _logger.LogWarning(
                        "scheduled_reindex_git_skip collection={Collection} detail={Detail}",
                        name,
                        message);
                    skipped++;
                    continue;
                }

                if (!changed)
                {
                    _logger.LogInformation(
                        "scheduled_reindex_unchanged collection={Collection} detail={Detail}",
                        name,
                        message);
                    skipped++;
                    continue;
                }

                _logger.LogInformation(
                    "scheduled_reindex_git_updated collection={Collection} detail={Detail}",
                    name,
                    message);
            }

            if (await _jobs.IsRunningAsync(name, cancellationToken).ConfigureAwait(false))
            {
                skipped++;
                continue;
            }

            try
            {
                var startResult = await _jobs.StartAsync(
                    new IndexCodebaseCommand(
                        name,
                        "/" + name,
                        Force: false,
                        Wait: true,
                        TimeoutSeconds: _reindex.IndexTimeoutSeconds),
                    cancellationToken).ConfigureAwait(false);

                startResult.Match(
                    onSuccess: _ => reindexed++,
                    onFailure: error =>
                    {
                        errors++;
                        _logger.LogError(
                            "scheduled_reindex_failed collection={Collection} code={Code} message={Message}",
                            name,
                            error.Code,
                            error.Message);
                    });
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex, "scheduled_reindex_failed collection={Collection}", name);
            }
        }

        _logger.LogInformation(
            "scheduled_reindex_done reindexed={Reindexed} skipped={Skipped} errors={Errors}",
            reindexed,
            skipped,
            errors);
    }

    private (bool Ok, bool Changed, string Message) TryGitPull(string repoPath)
    {
        try
        {
            if (!Repository.IsValid(repoPath))
            {
                return (false, false, "not a git repository");
            }

            using var repo = new Repository(repoPath);
            if (repo.RetrieveStatus().IsDirty)
            {
                return (false, false, "dirty working tree — skipping");
            }

            var branch = repo.Head.FriendlyName;
            if (branch is "HEAD" or null)
            {
                return (false, false, "detached HEAD — skipping");
            }

            var remote = repo.Network.Remotes["origin"];
            if (remote is null)
            {
                return (false, false, "no origin remote");
            }

            var oldSha = repo.Head.Tip.Sha;
            Commands.Fetch(
                repo,
                remote.Name,
                Array.Empty<string>(),
                new FetchOptions
                {
                    TagFetchMode = TagFetchMode.None,
                },
                null);

            var tracking = repo.Head.TrackedBranch;
            if (tracking is null)
            {
                return (false, false, $"no upstream for {branch}");
            }

            if (oldSha == tracking.Tip.Sha)
            {
                return (true, false, "no changes");
            }

            var signature = repo.Config.BuildSignature(DateTimeOffset.Now)
                ?? new Signature("codebase-indexer", "reindex@localhost", DateTimeOffset.Now);
            var result = Commands.Pull(
                repo,
                signature,
                new PullOptions
                {
                    FetchOptions = new FetchOptions(),
                    MergeOptions = new MergeOptions
                    {
                        FastForwardStrategy = FastForwardStrategy.FastForwardOnly,
                    },
                });

            if (result.Status == MergeStatus.Conflicts)
            {
                return (false, false, "merge conflicts — skipping");
            }

            var newSha = repo.Head.Tip.Sha;
            return (true, oldSha != newSha, $"updated {oldSha[..8]} -> {newSha[..8]}");
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }
}
