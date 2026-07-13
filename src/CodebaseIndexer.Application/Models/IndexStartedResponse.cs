using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Models;

/// <summary>Response when an index job was started successfully.</summary>
/// <param name="Message">Human-readable start message.</param>
/// <param name="Collection">Target collection name.</param>
/// <param name="Path">Sub-path within the workspace being indexed.</param>
/// <param name="Hint">Suggested next step for the caller.</param>
public sealed record IndexStartedResponse(string Message, string Collection, string Path, string Hint);
