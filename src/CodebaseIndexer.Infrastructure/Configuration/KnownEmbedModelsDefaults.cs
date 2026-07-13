namespace CodebaseIndexer.Infrastructure.Configuration;

internal static class KnownEmbedModelsDefaults
{
    public static IReadOnlyDictionary<string, int> MaxTokens { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["nomic-ai/nomic-embed-text-v1.5"] = 8192,
        ["BAAI/bge-base-en-v1.5"] = 512,
        ["BAAI/bge-small-en-v1.5"] = 512,
        ["jinaai/jina-embeddings-v2-base-code"] = 8192,
        ["Qwen/Qwen3-Embedding-0.6B"] = 32768,
        ["Qwen/Qwen3-Embedding-4B"] = 32768,
        ["Qwen/Qwen3-Embedding-8B"] = 32768,
        ["Alibaba-NLP/gte-modernbert-base"] = 8192,
        ["ibm-granite/granite-embedding-311m-multilingual-r2"] = 32768,
        ["ibm-granite/granite-embedding-97m-multilingual-r2"] = 32768,
        ["infly/inf-retriever-v1-1.5b"] = 32768,
    };
}
