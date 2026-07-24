using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Application.Services;

/// <summary>Vector discovery: recommend_code and find_outlier_chunks.</summary>
public interface IRecommendService
{
    /// <summary>Recommend chunks similar to positives / dissimilar from negatives.</summary>
    Task<Result<object>> RecommendCodeAsync(
        string collection,
        IReadOnlyList<string>? positiveChunkIds = null,
        string? positiveQuery = null,
        IReadOnlyList<string>? negativeChunkIds = null,
        string? negativeQuery = null,
        int limit = 5,
        SourceLanguage? language = null,
        string? pathGlob = null,
        int? maxContentChars = null,
        CancellationToken cancellationToken = default);

    /// <summary>Find chunks semantically distant from a context sample.</summary>
    Task<Result<object>> FindOutlierChunksAsync(
        string collection,
        IReadOnlyList<string>? contextChunkIds = null,
        int limit = 5,
        SourceLanguage? language = null,
        string? pathGlob = null,
        float? maxSimilarity = null,
        int? maxContentChars = null,
        CancellationToken cancellationToken = default);
}
