using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Models;

/// <summary>Response when no index jobs are tracked.</summary>
/// <param name="Message">Human-readable empty-state message.</param>
public sealed record IndexStatusEmptyResponse(string Message);
