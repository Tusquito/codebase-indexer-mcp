using System.Collections.Concurrent;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Embedding;

/// <summary>ONNX-based sparse embedder using BM25-style fastembed models.</summary>
public sealed class OnnxSparseEmbedder : ISparseEmbedder, IDisposable
{
    private static readonly ConcurrentDictionary<string, Lazy<Bm25EmbedderCore>> SharedModels = new();

    private readonly EmbeddingOptions _embedding;
    private readonly KnownEmbedModelsOptions _knownEmbedModels;
    private readonly ILogger<OnnxSparseEmbedder> _logger;
    private bool _ready;
    private int _maxTokens;
    private TruncationSource _truncationSource = TruncationSource.Disabled;

    /// <summary>Creates a sparse embedder from embedding and known-model options.</summary>
    public OnnxSparseEmbedder(
        IOptions<EmbeddingOptions> embedding,
        IOptions<KnownEmbedModelsOptions> knownEmbedModels,
        ILogger<OnnxSparseEmbedder> logger)
    {
        _embedding = embedding.Value;
        _knownEmbedModels = knownEmbedModels.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsLoaded => _ready;

    /// <inheritdoc />
    public Task<Result> PreloadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var modelDir = SparseModelCacheResolver.ResolveModelDirectory(_embedding.CachePath, _embedding.SparseModel);
            _ = GetSharedModel(modelDir);
            var tokenLimit = EmbeddingTruncation.ResolveMaxEmbedTokens(
                EmbedRole.Sparse,
                _embedding.SparseModel,
                _embedding.MaxSparseTokens,
                modelDir,
                _knownEmbedModels.FrozenMaxTokens,
                _logger);
            _maxTokens = tokenLimit.MaxTokens;
            _truncationSource = tokenLimit.Source;
            _ready = true;
            _logger.LogInformation(
                "sparse_embed_ready model={Model} dir={Dir}",
                _embedding.SparseModel,
                modelDir);
            return Task.FromResult(Result.Success());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure(new Error(
                ErrorKind.Dependency,
                EmbedErrorCodes.Sparse,
                $"Sparse preload failed: {ex.Message}")));
        }
    }

    /// <inheritdoc />
    public void Release()
    {
        _ready = false;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SparseVector>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return Task.FromResult(Result<IReadOnlyList<SparseVector>>.Success(Array.Empty<SparseVector>()));
        }

        try
        {
            var model = GetSharedModel(
                SparseModelCacheResolver.ResolveModelDirectory(_embedding.CachePath, _embedding.SparseModel));
            var results = new List<SparseVector>(texts.Count);
            foreach (var text in texts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(model.Embed(Truncate(text)));
            }

            return Task.FromResult(Result<IReadOnlyList<SparseVector>>.Success(results));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<IReadOnlyList<SparseVector>>.Failure(new Error(
                ErrorKind.Dependency,
                EmbedErrorCodes.Sparse,
                $"Sparse embed failed: {ex.Message}")));
        }
    }

    /// <summary>Releases no shared models; they live for the process lifetime.</summary>
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

        return EmbeddingTruncation.TruncateBm25Text(text, _maxTokens).Text;
    }

    private Bm25EmbedderCore GetSharedModel(string modelDir) =>
        SharedModels.GetOrAdd(
            $"{_embedding.CachePath}|{_embedding.SparseModel}",
            _ => new Lazy<Bm25EmbedderCore>(() => new Bm25EmbedderCore(modelDir), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
}
