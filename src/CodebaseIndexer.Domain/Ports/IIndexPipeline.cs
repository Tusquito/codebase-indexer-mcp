using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for running the end-to-end codebase indexing pipeline.</summary>
public interface IIndexPipeline
{
    /// <summary>Indexes files under a path into a vector collection.</summary>
    /// <param name="collection">Target collection name.</param>
    /// <param name="subPath">Repository-relative subdirectory to index.</param>
    /// <param name="force">Whether to force re-indexing regardless of file timestamps.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// <see cref="Result{T}.Success"/> with <see cref="PipelineResult"/> for a completed run
    /// (including runs that collected partial step <see cref="PipelineResult.Errors"/>);
    /// <see cref="Result{T}.Failure"/> for an expected total failure;
    /// cooperative cancel via <see cref="OperationCanceledException"/>.
    /// </returns>
    Task<Result<PipelineResult>> RunAsync(
        string collection,
        string subPath,
        bool force,
        CancellationToken cancellationToken);
}
