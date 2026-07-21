namespace CodebaseIndexer.Domain.Models;

/// <summary>RESOLVES_TO edge row (artifact → collection).</summary>
/// <param name="ArtifactKey">Artifact key.</param>
/// <param name="TargetCollection">Matched collection name.</param>
public sealed record GraphResolvesToRow(string ArtifactKey, string TargetCollection);
