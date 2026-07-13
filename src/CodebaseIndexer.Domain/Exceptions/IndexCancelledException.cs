namespace CodebaseIndexer.Domain.Exceptions;

/// <summary>Exception thrown when an indexing job is cancelled.</summary>
/// <param name="message">Description of why indexing was cancelled.</param>
public sealed class IndexCancelledException(string message) : Exception(message);
