namespace CodebaseIndexer.Domain.Exceptions;

/// <summary>Exception thrown when dense or sparse embedding operations fail.</summary>
/// <param name="message">Description of the embedding failure.</param>
/// <param name="innerException">Optional underlying exception.</param>
public sealed class EmbeddingException(string message, Exception? innerException = null)
    : Exception(message, innerException);
