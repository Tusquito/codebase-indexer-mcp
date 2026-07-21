namespace CodebaseIndexer.Application.Services;

/// <summary>Graph-augmented retrieval use case (expand_search_context).</summary>
public interface IExpandSearchContextService
{
    /// <summary>Hybrid search seeds → Neo4j neighborhood → hydrate related chunks.</summary>
    Task<object> ExpandSearchContextAsync(
        string query,
        int topK = 5,
        string? collection = null,
        IReadOnlyList<string>? collections = null,
        int? graphHops = null,
        string? language = null,
        float minScore = 0.5f,
        int? maxContentChars = null,
        CancellationToken cancellationToken = default);
}
