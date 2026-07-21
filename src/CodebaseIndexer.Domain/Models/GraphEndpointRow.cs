namespace CodebaseIndexer.Domain.Models;

/// <summary>Endpoint node row.</summary>
/// <param name="Path">URL/route path.</param>
/// <param name="Method">HTTP method when known.</param>
public sealed record GraphEndpointRow(string Path, string Method);
