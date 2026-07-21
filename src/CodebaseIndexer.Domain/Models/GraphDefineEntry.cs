namespace CodebaseIndexer.Domain.Models;

/// <summary>One DEFINES symbol candidate for call-target resolution.</summary>
/// <param name="QualifiedName">Stable symbol key.</param>
/// <param name="RelPath">Defining file path.</param>
/// <param name="Name">Symbol name.</param>
/// <param name="Kind">Symbol kind.</param>
public sealed record GraphDefineEntry(
    string QualifiedName,
    string RelPath,
    string Name,
    string Kind);
