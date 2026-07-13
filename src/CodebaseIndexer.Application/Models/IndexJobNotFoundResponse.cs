using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Models;

/// <summary>Response when the requested index job does not exist.</summary>
/// <param name="Error">Error description.</param>
public sealed record IndexJobNotFoundResponse(string Error);
