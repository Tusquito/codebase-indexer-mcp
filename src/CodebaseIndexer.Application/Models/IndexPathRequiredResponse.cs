using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Models;

/// <summary>Response when the index path argument is missing or invalid.</summary>
/// <param name="Error">Error description.</param>
/// <param name="Hint">Suggested next step for the caller.</param>
public sealed record IndexPathRequiredResponse(string Error, string Hint);
