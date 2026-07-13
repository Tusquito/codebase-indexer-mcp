using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Models;

/// <summary>Response when an index job is already running for the collection.</summary>
/// <param name="Message">Human-readable status message.</param>
/// <param name="Status">Current job snapshot.</param>
public sealed record IndexAlreadyRunningResponse(string Message, IndexJobSnapshot Status);
