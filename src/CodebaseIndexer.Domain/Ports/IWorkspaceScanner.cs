using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for scanning a workspace and yielding files to index.</summary>
public interface IWorkspaceScanner
{
    /// <summary>Enumerates files under a workspace path that require indexing.</summary>
    /// <param name="workspacePath">Absolute path to the workspace root.</param>
    /// <param name="subPath">Repository-relative subdirectory to scan.</param>
    /// <param name="existingMetadata">Previously stored file metadata for change detection, if available.</param>
    /// <param name="force">Whether to yield all files regardless of change detection.</param>
    /// <param name="cancellationToken">Token used to cancel the scan.</param>
    /// <returns>An async stream of files discovered for indexing.</returns>
    IAsyncEnumerable<FileRecord> ScanFilesAsync(
        string workspacePath,
        string subPath,
        IReadOnlyDictionary<string, FileMetadata>? existingMetadata,
        bool force,
        CancellationToken cancellationToken = default);
}
