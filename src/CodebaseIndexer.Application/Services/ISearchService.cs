using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Application.Services;

/// <summary>Hybrid search and symbol lookup use-case.</summary>
public interface ISearchService
{
    /// <summary>Embed query and run hybrid/dense search across collections.</summary>
    Task<Result<SearchCodebaseResponse>> SearchCodebaseAsync(
        string query,
        int topK = 5,
        string? collection = null,
        IReadOnlyList<string>? collections = null,
        SourceLanguage? language = null,
        float minScore = 0.5f,
        int? maxContentChars = null,
        bool? rerank = null,
        CancellationToken cancellationToken = default);

    /// <summary>Same search as codebase but metadata-only results.</summary>
    Task<Result<SearchSymbolsResponse>> SearchSymbolsAsync(
        string query,
        int topK = 10,
        string? collection = null,
        IReadOnlyList<string>? collections = null,
        SourceLanguage? language = null,
        float minScore = 0.4f,
        bool? rerank = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Read tools: chunk, outline, summary, list collections.</summary>
public interface ICollectionQueryService
{
    /// <summary>Retrieve a chunk by id.</summary>
    Task<Result<object>> GetChunkAsync(string chunkId, string? collection = null, CancellationToken cancellationToken = default);

    /// <summary>Symbol outline for a file.</summary>
    Task<Result<object>> GetFileOutlineAsync(string relPath, string? collection = null, CancellationToken cancellationToken = default);

    /// <summary>Collection orientation summary (includes build_dependencies when other collections exist).</summary>
    Task<Result<object>> GetCollectionSummaryAsync(string? collection = null, CancellationToken cancellationToken = default);

    /// <summary>List collections with stats.</summary>
    Task<Result<IReadOnlyList<CollectionListItem>>> ListCollectionsAsync(CancellationToken cancellationToken = default);
}
