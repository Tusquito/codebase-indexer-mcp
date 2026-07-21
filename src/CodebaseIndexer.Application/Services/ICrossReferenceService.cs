namespace CodebaseIndexer.Application.Services;

/// <summary>Cross-collection reference discovery (find_cross_references).</summary>
public interface ICrossReferenceService
{
    /// <summary>Find cross-project links for a query, symbol, and/or member call site.</summary>
    Task<object> FindCrossReferencesAsync(
        string? query = null,
        string? symbolName = null,
        IReadOnlyList<string>? collections = null,
        int topK = 10,
        string? member = null,
        string? receiver = null,
        bool? rerank = null,
        CancellationToken cancellationToken = default);
}
