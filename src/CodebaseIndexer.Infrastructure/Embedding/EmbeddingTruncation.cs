using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.ML.Tokenizers;

namespace CodebaseIndexer.Infrastructure.Embedding;

public static class EmbeddingTruncation
{
    public static EmbedTokenLimit ResolveMaxEmbedTokens(
        EmbedRole role,
        string modelName,
        int envTokens,
        string? modelDir,
        IReadOnlyDictionary<string, int> knownRegistry,
        ILogger? logger = null)
    {
        if (envTokens > 0)
        {
            logger?.LogInformation(
                "truncation_strategy {Role}_tokens={Tokens} source=env_override model={Model}",
                role, envTokens, modelName);
            return new EmbedTokenLimit(envTokens, TruncationSource.EnvOverride);
        }

        int? detected = null;
        if (!string.IsNullOrEmpty(modelDir))
        {
            detected = ReadModelMaxTokensFromDir(modelDir);
        }

        detected ??= knownRegistry.TryGetValue(modelName, out var known) ? known : null;

        if (detected is > 0)
        {
            logger?.LogInformation(
                "truncation_strategy {Role}_tokens={Tokens} source=model_auto_detect model={Model}",
                role, detected.Value, modelName);
            return new EmbedTokenLimit(detected.Value, TruncationSource.ModelAutoDetect);
        }

        if (role == EmbedRole.Sparse)
        {
            logger?.LogInformation(
                "truncation_strategy sparse_tokens=0 source=disabled model={Model}",
                modelName);
            return new EmbedTokenLimit(0, TruncationSource.Disabled);
        }

        logger?.LogWarning(
            "truncation_strategy dense_tokens=unset — no truncation until model is known model={Model}",
            modelName);
        return new EmbedTokenLimit(0, TruncationSource.Disabled);
    }

    public static int? ReadModelMaxTokensFromDir(string modelDir)
    {
        foreach (var name in new[] { "tokenizer_config.json", "config.json" })
        {
            var path = Path.Combine(modelDir, name);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var key in new[] { "model_max_length", "max_position_embeddings", "max_seq_length" })
                {
                    if (document.RootElement.TryGetProperty(key, out var value)
                        && value.ValueKind == JsonValueKind.Number
                        && value.TryGetInt32(out var tokens)
                        && tokens > 0)
                    {
                        return tokens;
                    }
                }
            }
            catch (Exception)
            {
                // fall through
            }
        }

        return null;
    }

    public static TruncatedText TruncateForEmbedding(string text, int maxTokens, Tokenizer? tokenizer)
    {
        if (maxTokens <= 0 || tokenizer is null)
        {
            return new TruncatedText(text, -1);
        }

        if (text.Length <= maxTokens * 2)
        {
            return new TruncatedText(text, -1);
        }

        var encoding = tokenizer.EncodeToTokens(text, out _);
        if (encoding.Count <= maxTokens)
        {
            return new TruncatedText(text, encoding.Count);
        }

        var truncated = tokenizer.Decode(encoding.Take(maxTokens).Select(t => t.Id));
        return new TruncatedText(truncated, maxTokens);
    }

    public static TruncatedText TruncateBm25Text(string text, int maxTokens)
    {
        if (maxTokens <= 0)
        {
            return new TruncatedText(text, -1);
        }

        var tokens = Bm25Tokenizer.Tokenize(text);
        if (tokens.Count <= maxTokens)
        {
            return new TruncatedText(text, tokens.Count);
        }

        return new TruncatedText(string.Join(' ', tokens.Take(maxTokens)), maxTokens);
    }
}
