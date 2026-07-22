using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Search;

/// <summary>Thin wrapper over <see cref="UrlExtractors"/> for legacy call sites.</summary>
public static class ReferenceClassifier
{
    private static readonly UrlExtractors Default = new(Array.Empty<string>());

    /// <summary>Classify how <paramref name="symbol"/> appears in <paramref name="content"/>.</summary>
    public static ReferenceType Classify(string content, string symbol, string relPath = "") =>
        Default.ClassifyReference(content, symbol, relPath);
}
