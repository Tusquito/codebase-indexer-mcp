using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Models;

public sealed record IndexPathRequiredResponse(string Error, string Hint);

public sealed record IndexAlreadyRunningResponse(string Message, IndexJobSnapshot Status);

public sealed record IndexStartedResponse(string Message, string Collection, string Path, string Hint);

public sealed record IndexStatusEmptyResponse(string Message);

public sealed record IndexJobNotFoundResponse(string Error);

public sealed record IndexJobNotRunningResponse(string Error, IndexJobSnapshot Status);

public sealed record IndexCancelledResponse(string Message, string Collection, string Hint);

public sealed record IndexAllEmptyResponse(string Error, string Hint);

public sealed record IndexAllCompletedResponse(string Message, IReadOnlyList<IndexJobSnapshot> Results);
