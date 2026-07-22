namespace CodebaseIndexer.Domain.Models;

/// <summary>DEFINES edge row (chunk → symbol).</summary>
/// <param name="ChunkId">Defining chunk.</param>
/// <param name="QualifiedName">Stable symbol key.</param>
/// <param name="Name">Display name.</param>
/// <param name="Kind">Symbol kind.</param>
public sealed record GraphDefineRow(string ChunkId, string QualifiedName, string Name, SymbolType Kind);
