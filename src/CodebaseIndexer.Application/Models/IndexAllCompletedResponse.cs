using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Models;

/// <summary>Response when index-all completes successfully.</summary>
/// <param name="Message">Human-readable completion message.</param>
/// <param name="Results">Snapshots of all jobs that were indexed.</param>
public sealed record IndexAllCompletedResponse(string Message, IReadOnlyList<IndexJobSnapshot> Results);
