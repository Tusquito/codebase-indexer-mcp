using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Models;

/// <summary>Response when cancel is requested for a job that is not running.</summary>
/// <param name="Error">Error description.</param>
/// <param name="Status">Current job snapshot.</param>
public sealed record IndexJobNotRunningResponse(string Error, IndexJobSnapshot Status);
