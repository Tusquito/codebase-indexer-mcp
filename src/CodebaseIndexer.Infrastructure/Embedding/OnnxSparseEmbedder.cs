using System.Collections.Concurrent;
using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Embedding;

public sealed class OnnxSparseEmbedder : ISparseEmbedder, IDisposable
{
    private static readonly ConcurrentDictionary<string, Lazy<Bm25EmbedderCore>> SharedModels = new();

    private readonly Settings _settings;
    private readonly ILogger<OnnxSparseEmbedder> _logger;
    private bool _ready;
    private int _maxTokens;
    private TruncationSource _truncationSource = TruncationSource.Disabled;

    public OnnxSparseEmbedder(IOptions<Settings> settings, ILogger<OnnxSparseEmbedder> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public string BackendName => "onnx";
    public bool IsLoaded => _ready;

    public Task PreloadAsync(CancellationToken cancellationToken = default)
    {
        var modelDir = ResolveModelDirectory(_settings.FastembedCachePath, _settings.SparseEmbedModel);
        _ = GetSharedModel(modelDir);
        (_maxTokens, _truncationSource) = EmbeddingTruncation.ResolveMaxEmbedTokens(
            "sparse",
            _settings.SparseEmbedModel,
            _settings.MaxSparseEmbedTokens,
            modelDir,
            KnownEmbedModels.MaxTokens,
            _logger);
        _ready = true;
        _logger.LogInformation(
            "sparse_embed_ready model={Model} dir={Dir}",
            _settings.SparseEmbedModel,
            modelDir);
        return Task.CompletedTask;
    }

    public void Release()
    {
        _ready = false;
    }

    public Task<IReadOnlyList<SparseVector>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<SparseVector>>(Array.Empty<SparseVector>());
        }

        var model = GetSharedModel(ResolveModelDirectory(_settings.FastembedCachePath, _settings.SparseEmbedModel));
        var results = new List<SparseVector>(texts.Count);
        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(model.Embed(Truncate(text)));
        }

        return Task.FromResult<IReadOnlyList<SparseVector>>(results);
    }

    public void Dispose()
    {
        // Shared models live for process lifetime.
    }

    private string Truncate(string text)
    {
        if (_maxTokens <= 0)
        {
            return text;
        }

        var (truncated, _) = EmbeddingTruncation.TruncateBm25Text(text, _maxTokens);
        return truncated;
    }

    private Bm25EmbedderCore GetSharedModel(string modelDir) =>
        SharedModels.GetOrAdd(
            $"{_settings.FastembedCachePath}|{_settings.SparseEmbedModel}",
            _ => new Lazy<Bm25EmbedderCore>(() => new Bm25EmbedderCore(modelDir), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    private static string ResolveModelDirectory(string cacheRoot, string modelName)
    {
        var normalized = modelName.Replace('/', Path.DirectorySeparatorChar);
        var direct = Path.Combine(cacheRoot, normalized);
        if (Directory.Exists(direct))
        {
            return direct;
        }

        if (!Directory.Exists(cacheRoot))
        {
            throw new EmbeddingException($"Fastembed cache directory '{cacheRoot}' does not exist.");
        }

        var match = Directory.EnumerateDirectories(cacheRoot, "*", SearchOption.AllDirectories)
            .FirstOrDefault(d => d.Replace('\\', '/').EndsWith(modelName, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        throw new EmbeddingException($"Sparse model '{modelName}' not found under '{cacheRoot}'.");
    }
}
