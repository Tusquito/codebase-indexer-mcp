namespace CodebaseIndexer.Domain.Models;

/// <summary>CALLS edge row (chunk → callee symbol) with call_token.</summary>
/// <param name="ChunkId">Caller chunk.</param>
/// <param name="QualifiedName">Resolved or stub symbol key.</param>
/// <param name="Name">Display name.</param>
/// <param name="CallToken">Raw callee token from the chunk.</param>
public sealed record GraphCallRow(string ChunkId, string QualifiedName, string Name, string CallToken);
