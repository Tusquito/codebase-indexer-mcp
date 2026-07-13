using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Models;

/// <summary>Response when an index job was cancelled.</summary>
/// <param name="Message">Human-readable cancellation message.</param>
/// <param name="Collection">Collection name of the cancelled job.</param>
/// <param name="Hint">Suggested next step for the caller.</param>
public sealed record IndexCancelledResponse(string Message, string Collection, string Hint);
