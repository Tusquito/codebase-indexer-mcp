using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Services;

/// <summary>Hybrid search and symbol lookup use-case.</summary>
public interface ISearchService
{
    /// <summary>Embed query and run hybrid/dense search across collections.</summary>
    Task<SearchCodebaseResponse> SearchCodebaseAsync(
        string query,
        int topK = 5,
        string? collection = null,
        IReadOnlyList<string>? collections = null,
        string? language = null,
        float minScore = 0.5f,
        int? maxContentChars = null,
        bool? rerank = null,
        CancellationToken cancellationToken = default);

    /// <summary>Same search as codebase but metadata-only results.</summary>
    Task<SearchSymbolsResponse> SearchSymbolsAsync(
        string query,
        int topK = 10,
        string? collection = null,
        IReadOnlyList<string>? collections = null,
        string? language = null,
        float minScore = 0.4f,
        bool? rerank = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Read tools: chunk, outline, summary, list collections.</summary>
public interface ICollectionQueryService
{
    /// <summary>Retrieve a chunk by id.</summary>
    Task<object> GetChunkAsync(string chunkId, string? collection = null, CancellationToken cancellationToken = default);

    /// <summary>Symbol outline for a file.</summary>
    Task<object> GetFileOutlineAsync(string relPath, string? collection = null, CancellationToken cancellationToken = default);

    /// <summary>Collection orientation summary (includes build_dependencies when other collections exist).</summary>
    Task<object> GetCollectionSummaryAsync(string? collection = null, CancellationToken cancellationToken = default);

    /// <summary>List collections with stats.</summary>
    Task<IReadOnlyList<CollectionListItem>> ListCollectionsAsync(CancellationToken cancellationToken = default);
}
