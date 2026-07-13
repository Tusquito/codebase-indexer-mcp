using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Models;

/// <summary>Response when index-all finds no collections to index.</summary>
/// <param name="Error">Error description.</param>
/// <param name="Hint">Suggested next step for the caller.</param>
public sealed record IndexAllEmptyResponse(string Error, string Hint);
