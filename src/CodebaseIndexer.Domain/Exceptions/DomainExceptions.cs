namespace CodebaseIndexer.Domain.Exceptions;

public sealed class EmbeddingException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class VectorStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
