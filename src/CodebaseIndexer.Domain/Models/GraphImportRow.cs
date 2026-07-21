namespace CodebaseIndexer.Domain.Models;

/// <summary>IMPORTS edge row (file → import symbol).</summary>
/// <param name="RelPath">Importing file path.</param>
/// <param name="QualifiedName">Import symbol key.</param>
/// <param name="Name">Imported name.</param>
public sealed record GraphImportRow(string RelPath, string QualifiedName, string Name);
