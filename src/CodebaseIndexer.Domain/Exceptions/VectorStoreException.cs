namespace CodebaseIndexer.Domain.Exceptions;

/// <summary>Exception thrown when vector store operations fail.</summary>
/// <param name="message">Description of the vector store failure.</param>
/// <param name="innerException">Optional underlying exception.</param>
public sealed class VectorStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
