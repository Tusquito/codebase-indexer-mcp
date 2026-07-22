using System.Buffers;
using System.Security.Cryptography;
using System.Threading.Channels;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Indexing;

/// <summary>Parallel workspace file scanner with gitignore filtering and incremental hashing.</summary>
public sealed class WorkspaceScanner : IWorkspaceScanner
{
    private static readonly HashSet<string> DefaultExcludedDirs = new(StringComparer.Ordinal)
    {
        "node_modules", ".git", "__pycache__", ".venv", ".venv-bench", ".venv-train", "venv",
        "dist", "build", "target", "bin", "obj", ".gradle", ".mypy_cache", ".pytest_cache",
        ".ruff_cache", ".idea", ".vscode", "migrations",
    };

    private readonly WorkspaceOptions _options;
    private readonly ILogger<WorkspaceScanner> _logger;

    /// <summary>Creates a scanner from workspace options.</summary>
    /// <param name="options">Workspace scan configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public WorkspaceScanner(IOptions<WorkspaceOptions> options, ILogger<WorkspaceScanner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FileRecord> ScanFilesAsync(
        string workspacePath,
        string subPath,
        IReadOnlyDictionary<string, FileMetadata>? existingMetadata,
        bool force,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var workspace = Path.GetFullPath(workspacePath);
        var scanRoot = Path.GetFullPath(Path.Combine(workspace, subPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        if (scanRoot != workspace && !scanRoot.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            _logger.LogError("scan_path_outside_workspace workspace={Workspace} path={Path}", workspace, scanRoot);
            yield break;
        }

        if (!Directory.Exists(scanRoot))
        {
            _logger.LogError("scan_path_not_found path={Path}", scanRoot);
            yield break;
        }

        var excluded = ParseExcludedDirs(_options.ExcludedDirs);
        var metadata = force ? null : existingMetadata;
        var dop = Math.Max(1, _options.HashWorkerDop);
        var capacity = Math.Max(1, _options.ReadaheadBuffer);

        var pathChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = dop == 1,
        });

        var outputChannel = Channel.CreateBounded<FileRecord?>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = dop == 1,
            SingleReader = true,
        });

        var rootGitignore = GitIgnoreMatcher.Load(workspace, ".gitignore");
        var rootCodeindexignore = GitIgnoreMatcher.Load(workspace, ".codeindexignore");
        GitIgnoreMatcher? projGitignore = null;
        GitIgnoreMatcher? projCodeindexignore = null;
        if (!string.Equals(scanRoot, workspace, StringComparison.Ordinal))
        {
            projGitignore = GitIgnoreMatcher.Load(scanRoot, ".gitignore");
            projCodeindexignore = GitIgnoreMatcher.Load(scanRoot, ".codeindexignore");
        }

        var producer = ProducePathsAsync(
            workspace,
            scanRoot,
            excluded,
            rootGitignore,
            rootCodeindexignore,
            projGitignore,
            projCodeindexignore,
            pathChannel.Writer,
            cancellationToken);

        var workers = Enumerable.Range(0, dop)
            .Select(_ => HashWorkerAsync(
                workspace,
                scanRoot,
                metadata,
                pathChannel.Reader,
                outputChannel.Writer,
                cancellationToken))
            .ToArray();

        _ = Task.WhenAll(workers).ContinueWith(
            _ => outputChannel.Writer.TryComplete(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await foreach (var record in ReadOutputAsync(outputChannel.Reader, producer, cancellationToken))
        {
            yield return record;
        }
    }

    private static HashSet<string> ParseExcludedDirs(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return DefaultExcludedDirs;
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(part);
        }

        return set.Count > 0 ? set : DefaultExcludedDirs;
    }

    private static bool ShouldPruneDir(string dirname, HashSet<string> excluded) =>
        excluded.Contains(dirname) || dirname.StartsWith(".venv", StringComparison.Ordinal);

    private static async Task ProducePathsAsync(
        string workspace,
        string scanRoot,
        HashSet<string> excluded,
        GitIgnoreMatcher? rootGitignore,
        GitIgnoreMatcher? rootCodeindexignore,
        GitIgnoreMatcher? projGitignore,
        GitIgnoreMatcher? projCodeindexignore,
        ChannelWriter<string> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var absPath in WalkFiles(scanRoot, excluded))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relPath = Path.GetRelativePath(workspace, absPath).Replace('\\', '/');
                var relToScan = Path.GetRelativePath(scanRoot, absPath).Replace('\\', '/');

                if (rootGitignore?.IsIgnored(relPath) == true
                    || rootCodeindexignore?.IsIgnored(relPath) == true
                    || projGitignore?.IsIgnored(relToScan) == true
                    || projCodeindexignore?.IsIgnored(relToScan) == true)
                {
                    continue;
                }

                var fileName = Path.GetFileName(absPath);
                var suffixes = GetSuffixes(absPath);
                var language = LanguageRegistry.DetectLanguage(fileName, suffixes);
                if (language is null)
                {
                    continue;
                }

                await writer.WriteAsync(absPath, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static IEnumerable<string> WalkFiles(string scanRoot, HashSet<string> excluded)
    {
        var stack = new Stack<string>();
        stack.Push(scanRoot);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            var relDir = Path.GetRelativePath(scanRoot, dir).Replace('\\', '/');
            var isGithub = relDir == ".github" || relDir.EndsWith("/.github", StringComparison.Ordinal);

            IEnumerable<string> subdirs;
            IEnumerable<string> files;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var subdir in subdirs)
            {
                var name = Path.GetFileName(subdir);
                if (ShouldPruneDir(name, excluded))
                {
                    continue;
                }

                if (isGithub && !string.Equals(name, "workflows", StringComparison.Ordinal))
                {
                    continue;
                }

                stack.Push(subdir);
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static List<string> GetSuffixes(string path)
    {
        var suffixes = new List<string>();
        var name = Path.GetFileName(path);
        var idx = 0;
        while (true)
        {
            var dot = name.IndexOf('.', idx);
            if (dot < 0)
            {
                break;
            }

            suffixes.Add(name[dot..]);
            idx = dot + 1;
        }

        return suffixes;
    }

    private static async Task HashWorkerAsync(
        string workspace,
        string scanRoot,
        IReadOnlyDictionary<string, FileMetadata>? metadata,
        ChannelReader<string> reader,
        ChannelWriter<FileRecord?> writer,
        CancellationToken cancellationToken)
    {
        await foreach (var absPath in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var relPath = Path.GetRelativePath(workspace, absPath).Replace('\\', '/');
            var fileName = Path.GetFileName(absPath);
            var language = LanguageRegistry.DetectLanguage(fileName, GetSuffixes(absPath));
            if (language is null)
            {
                continue;
            }

            var sourceLanguage = language.Value;

            double mtime;
            try
            {
                mtime = File.GetLastWriteTimeUtc(absPath).Subtract(DateTime.UnixEpoch).TotalSeconds;
            }
            catch (Exception ex)
            {
                _staticLogger?.LogWarning(ex, "file_stat_error path={Path}", relPath);
                continue;
            }

            if (metadata is not null
                && metadata.TryGetValue(relPath, out var stored)
                && stored.Mtime.HasValue
                && Math.Abs(stored.Mtime.Value - mtime) < 0.001)
            {
                await writer.WriteAsync(
                    new FileRecord(absPath, relPath, sourceLanguage, string.Empty, stored.Sha256, mtime, MtimeSkipped: true),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            byte[] raw;
            try
            {
                raw = await ReadFileBytesAsync(absPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _staticLogger?.LogWarning(ex, "file_read_error path={Path}", relPath);
                continue;
            }

            if (IsBinary(raw))
            {
                continue;
            }

            var content = System.Text.Encoding.UTF8.GetString(raw);
            var hash = ComputeSha256(raw);
            await writer.WriteAsync(
                new FileRecord(absPath, relPath, sourceLanguage, content, hash, mtime),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static ILogger? _staticLogger;

    internal static void SetLogger(ILogger logger) => _staticLogger = logger;

    private static async IAsyncEnumerable<FileRecord> ReadOutputAsync(
        ChannelReader<FileRecord?> reader,
        Task producer,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (item is not null)
            {
                yield return item;
            }
        }

        await producer.ConfigureAwait(false);
    }

    private static bool IsBinary(ReadOnlySpan<byte> data)
    {
        var limit = Math.Min(data.Length, 512);
        for (var i = 0; i < limit; i++)
        {
            if (data[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<byte[]> ReadFileBytesAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        var length = (int)Math.Min(stream.Length, int.MaxValue);
        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        var rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var offset = 0;
            while (offset < length)
            {
                var read = await stream.ReadAsync(rented.AsMemory(offset, length - offset), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            var result = new byte[offset];
            rented.AsSpan(0, offset).CopyTo(result);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static string ComputeSha256(ReadOnlySpan<byte> raw) =>
        Convert.ToHexStringLower(SHA256.HashData(raw));
}
