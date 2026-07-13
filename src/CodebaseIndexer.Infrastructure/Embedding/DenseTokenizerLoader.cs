using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.ML.Tokenizers;

namespace CodebaseIndexer.Infrastructure.Embedding;

/// <summary>Loads Hugging Face tokenizers from the local model cache for dense truncation.</summary>
public static class DenseTokenizerLoader
{
    /// <summary>Loads a tokenizer for the given model from the Hugging Face hub cache.</summary>
    /// <param name="modelId">Hugging Face model identifier (e.g. org/model).</param>
    /// <param name="logger">Optional logger for load failures.</param>
    /// <returns>Tokenizer instance, or null when files are unavailable.</returns>
    public static Tokenizer? LoadDenseTokenizer(string modelId, ILogger? logger = null)
    {
        var snapshotDir = ResolveModelSnapshotDir(modelId);
        if (snapshotDir is null)
        {
            logger?.LogWarning(
                "dense_tokenizer_load_failed model={Model} reason=snapshot_not_found",
                modelId);
            return null;
        }

        var tokenizerJson = Path.Combine(snapshotDir, "tokenizer.json");
        if (File.Exists(tokenizerJson))
        {
            try
            {
                return LoadFromTokenizerJson(tokenizerJson);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "dense_tokenizer_load_failed model={Model} path={Path}",
                    modelId,
                    tokenizerJson);
            }
        }

        var vocabJson = Path.Combine(snapshotDir, "vocab.json");
        var mergesTxt = Path.Combine(snapshotDir, "merges.txt");
        if (File.Exists(vocabJson) && File.Exists(mergesTxt))
        {
            try
            {
                using var vocabStream = File.OpenRead(vocabJson);
                using var mergesStream = File.OpenRead(mergesTxt);
                return BpeTokenizer.Create(vocabStream, mergesStream);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "dense_tokenizer_load_failed model={Model} path={Path}",
                    modelId,
                    snapshotDir);
            }
        }

        var vocabTxt = Path.Combine(snapshotDir, "vocab.txt");
        if (File.Exists(vocabTxt))
        {
            try
            {
                using var vocabStream = File.OpenRead(vocabTxt);
                return BertTokenizer.Create(vocabStream, new BertOptions());
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "dense_tokenizer_load_failed model={Model} path={Path}",
                    modelId,
                    vocabTxt);
            }
        }

        logger?.LogWarning(
            "dense_tokenizer_unavailable model={Model} reason=no_tokenizer_files",
            modelId);
        return null;
    }

    /// <summary>Resolves the Hugging Face hub cache directory from environment variables.</summary>
    /// <returns>Cache directory path, or null when not configured or missing.</returns>
    public static string? ResolveHfCacheDir()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HF_HUB_CACHE")))
        {
            return Environment.GetEnvironmentVariable("HF_HUB_CACHE");
        }

        var hfHome = Environment.GetEnvironmentVariable("HF_HOME");
        if (!string.IsNullOrEmpty(hfHome))
        {
            return Path.Combine(hfHome, "hub");
        }

        var transformersCache = Environment.GetEnvironmentVariable("TRANSFORMERS_CACHE");
        if (!string.IsNullOrEmpty(transformersCache))
        {
            return transformersCache;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultCache = Path.Combine(home, ".cache", "huggingface", "hub");
        return Directory.Exists(defaultCache) ? defaultCache : null;
    }

    private static string? ResolveModelSnapshotDir(string modelId)
    {
        var cacheDir = ResolveHfCacheDir();
        if (cacheDir is null)
        {
            return null;
        }

        var modelDirName = "models--" + modelId.Replace('/', '-');
        var modelRoot = Path.Combine(cacheDir, modelDirName);
        if (!Directory.Exists(modelRoot))
        {
            return null;
        }

        var snapshots = Path.Combine(modelRoot, "snapshots");
        if (!Directory.Exists(snapshots))
        {
            return null;
        }

        return Directory.EnumerateDirectories(snapshots).FirstOrDefault();
    }

    private static Tokenizer LoadFromTokenizerJson(string tokenizerJsonPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(tokenizerJsonPath));
        var model = document.RootElement.GetProperty("model");
        if (!model.TryGetProperty("vocab", out var vocabElement)
            || !model.TryGetProperty("merges", out var mergesElement))
        {
            throw new InvalidOperationException("tokenizer.json missing BPE vocab/merges.");
        }

        var vocabDict = vocabElement.EnumerateObject()
            .ToDictionary(static p => p.Name, static p => p.Value.GetInt32());

        using var vocabStream = new MemoryStream();
        JsonSerializer.Serialize(vocabStream, vocabDict);
        vocabStream.Position = 0;

        using var mergesStream = new MemoryStream();
        using (var writer = new StreamWriter(mergesStream, leaveOpen: true))
        {
            writer.WriteLine("#version: 0.2");
            foreach (var merge in mergesElement.EnumerateArray())
            {
                writer.WriteLine(merge.GetString());
            }

            writer.Flush();
        }

        mergesStream.Position = 0;
        return BpeTokenizer.Create(vocabStream, mergesStream);
    }
}
