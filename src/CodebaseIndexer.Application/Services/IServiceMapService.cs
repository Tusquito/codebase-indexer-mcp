namespace CodebaseIndexer.Application.Services;

/// <summary>Multi-collection service dependency mapping.</summary>
public interface IServiceMapService
{
    /// <summary>Discover HTTP/config/build edges across collections.</summary>
    Task<object> MapServiceDependenciesAsync(
        IReadOnlyList<string>? collections = null,
        int topK = 30,
        bool? rerank = null,
        CancellationToken cancellationToken = default);
}
