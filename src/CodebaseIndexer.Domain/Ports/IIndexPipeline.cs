using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for running the end-to-end codebase indexing pipeline.</summary>
public interface IIndexPipeline
{
    /// <summary>Indexes files under a path into a vector collection.</summary>
    /// <param name="collection">Target collection name.</param>
    /// <param name="subPath">Repository-relative subdirectory to index.</param>
    /// <param name="force">Whether to force re-indexing regardless of file timestamps.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Metrics describing the completed indexing run.</returns>
    Task<PipelineResult> RunAsync(
        string collection,
        string subPath,
        bool force,
        CancellationToken cancellationToken);
}
