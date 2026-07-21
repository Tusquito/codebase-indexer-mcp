namespace CodebaseIndexer.Domain.Models;

/// <summary>CONFIGURES edge row (config chunk → endpoint).</summary>
/// <param name="ChunkId">Config chunk.</param>
/// <param name="Path">Configured endpoint path.</param>
public sealed record GraphConfiguresRow(string ChunkId, string Path);
