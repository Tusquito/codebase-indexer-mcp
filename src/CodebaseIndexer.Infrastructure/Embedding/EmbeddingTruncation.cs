using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.ML.Tokenizers;

namespace CodebaseIndexer.Infrastructure.Embedding;

/// <summary>Resolves and applies token limits for dense and sparse embedding input.</summary>
public static class EmbeddingTruncation
{
    /// <summary>Resolves the maximum embed token count for a model and role.</summary>
    /// <param name="role">Dense or sparse embedding role.</param>
    /// <param name="modelName">Model identifier.</param>
    /// <param name="envTokens">Configured override; values above zero take precedence.</param>
    /// <param name="modelDir">Optional on-disk model directory for auto-detection.</param>
    /// <param name="knownRegistry">Known model name to token limit mapping.</param>
    /// <param name="logger">Optional logger for truncation strategy events.</param>
    /// <returns>Resolved token limit and its source.</returns>
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

    /// <summary>Reads maximum sequence length from Hugging Face model config files.</summary>
    /// <param name="modelDir">Directory containing tokenizer_config.json or config.json.</param>
    /// <returns>Detected token limit, or null when not found.</returns>
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

    /// <summary>Truncates text to a token limit using a Hugging Face tokenizer.</summary>
    /// <param name="text">Input text.</param>
    /// <param name="maxTokens">Maximum token count; non-positive values skip truncation.</param>
    /// <param name="tokenizer">Tokenizer instance, or null to skip truncation.</param>
    /// <returns>Truncated text and measured token count.</returns>
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

        var truncated = tokenizer.Decode(CollectTokenIds(encoding, maxTokens));
        return new TruncatedText(truncated, maxTokens);
    }

    private static int[] CollectTokenIds(IReadOnlyList<EncodedToken> encoding, int maxTokens)
    {
        var ids = new int[maxTokens];
        for (var i = 0; i < maxTokens; i++)
        {
            ids[i] = encoding[i].Id;
        }

        return ids;
    }

    /// <summary>Truncates text to a BM25 token limit using whitespace tokenization.</summary>
    /// <param name="text">Input text.</param>
    /// <param name="maxTokens">Maximum token count; non-positive values skip truncation.</param>
    /// <returns>Truncated text and measured token count.</returns>
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

        return new TruncatedText(JoinTokens(tokens, maxTokens), maxTokens);
    }

    private static string JoinTokens(IReadOnlyList<string> tokens, int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        var length = 0;
        for (var i = 0; i < count; i++)
        {
            length += tokens[i].Length;
            if (i > 0)
            {
                length++;
            }
        }

        return string.Create(length, (tokens, count), static (span, state) =>
        {
            var first = true;
            for (var i = 0; i < state.count; i++)
            {
                if (!first)
                {
                    span[0] = ' ';
                    span = span[1..];
                }

                var token = state.tokens[i];
                token.AsSpan().CopyTo(span);
                span = span[token.Length..];
                first = false;
            }
        });
    }
}
