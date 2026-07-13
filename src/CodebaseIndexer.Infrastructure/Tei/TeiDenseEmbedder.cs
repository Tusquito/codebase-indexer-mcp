using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using CodebaseIndexer.Infrastructure.Embedding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;

namespace CodebaseIndexer.Infrastructure.Tei;

/// <summary>Dense embedder that calls a Text Embeddings Inference (TEI) HTTP service.</summary>
public sealed class TeiDenseEmbedder : IDenseEmbedder
{
    private readonly ITeiEmbeddingsApi _api;
    private readonly TeiOptions _tei;
    private readonly EmbeddingOptions _embedding;
    private readonly KnownEmbedModelsOptions _knownEmbedModels;
    private readonly ILogger<TeiDenseEmbedder> _logger;
    private bool _ready;
    private int _maxTokens;
    private Tokenizer? _tokenizer;

    /// <summary>Creates a TEI-backed dense embedder.</summary>
    /// <param name="api">Refit client for the TEI embeddings API.</param>
    /// <param name="tei">TEI service options.</param>
    /// <param name="embedding">Embedding model configuration.</param>
    /// <param name="knownEmbedModels">Known model token limit registry.</param>
    /// <param name="logger">Logger instance.</param>
    public TeiDenseEmbedder(
        ITeiEmbeddingsApi api,
        IOptions<TeiOptions> tei,
        IOptions<EmbeddingOptions> embedding,
        IOptions<KnownEmbedModelsOptions> knownEmbedModels,
        ILogger<TeiDenseEmbedder> logger)
    {
        _api = api;
        _tei = tei.Value;
        _embedding = embedding.Value;
        _knownEmbedModels = knownEmbedModels.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public int VectorSize => _embedding.DenseVectorSize;

    /// <inheritdoc />
    public bool IsLoaded => _ready;

    /// <inheritdoc />
    public async Task PreloadAsync(CancellationToken cancellationToken = default)
    {
        using var healthResponse = await _api.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        healthResponse.EnsureSuccessStatusCode();

        var probe = await _api.CreateEmbeddingsAsync(
            new EmbeddingsRequest(_embedding.DenseModel, ["."], _tei.MrlDimensions),
            cancellationToken).ConfigureAwait(false);

        var embedding = probe.Data.OrderBy(d => d.Index).First().Embedding;
        if (embedding.Count != VectorSize)
        {
            throw new EmbeddingException(
                $"TEI model '{_embedding.DenseModel}' returned dimension {embedding.Count}, expected {VectorSize}.");
        }

        _ready = true;
        var tokenLimit = EmbeddingTruncation.ResolveMaxEmbedTokens(
            EmbedRole.Dense,
            _embedding.DenseModel,
            _embedding.MaxDenseTokens,
            modelDir: null,
            _knownEmbedModels.FrozenMaxTokens,
            _logger);
        _maxTokens = tokenLimit.MaxTokens;
        EnsureTruncation();
        _logger.LogInformation("TEI dense embedder ready at {TeiUrl} for model {Model}", _tei.Url, _embedding.DenseModel);
    }

    /// <inheritdoc />
    public void Release() => _ready = false;

    /// <inheritdoc />
    public Task<IReadOnlyList<IReadOnlyList<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default) =>
        EmbedInternalAsync(texts, isQuery: false, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<IReadOnlyList<float>>> EmbedQueryAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default) =>
        EmbedInternalAsync(texts, isQuery: true, cancellationToken);

    private async Task<IReadOnlyList<IReadOnlyList<float>>> EmbedInternalAsync(
        IReadOnlyList<string> texts,
        bool isQuery,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return Array.Empty<IReadOnlyList<float>>();
        }

        var results = new List<IReadOnlyList<float>>(texts.Count);
        var batchSize = Math.Max(1, _tei.EmbedBatchSize);

        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var count = Math.Min(batchSize, texts.Count - offset);
            var batch = new string[count];
            for (var i = 0; i < count; i++)
            {
                var text = texts[offset + i];
                batch[i] = isQuery ? ApplyQueryInstruction(text) : Truncate(text);
            }

            var response = await _api.CreateEmbeddingsAsync(
                new EmbeddingsRequest(_embedding.DenseModel, batch, _tei.MrlDimensions),
                cancellationToken).ConfigureAwait(false);

            var ordered = new IReadOnlyList<float>[count];
            foreach (var item in response.Data)
            {
                if ((uint)item.Index >= (uint)count)
                {
                    throw new EmbeddingException($"TEI returned out-of-range embedding index {item.Index}.");
                }

                ordered[item.Index] = Normalize(item.Embedding);
            }

            for (var i = 0; i < count; i++)
            {
                if (ordered[i] is null)
                {
                    throw new EmbeddingException($"TEI returned incomplete embeddings for {count} inputs.");
                }
            }

            for (var i = 0; i < count; i++)
            {
                results.Add(ordered[i]!);
            }
        }

        return results;
    }

    private void EnsureTruncation()
    {
        if (_tokenizer is not null)
        {
            return;
        }

        var modelId = _embedding.DenseModel;
        _tokenizer = DenseTokenizerLoader.LoadDenseTokenizer(modelId, _logger);
        if (_tokenizer is null)
        {
            _logger.LogWarning(
                "tei_dense_truncation_disabled model={Model} reason=tokenizer_unavailable",
                modelId);
        }
        else
        {
            _logger.LogInformation("dense_tokenizer_loaded model={Model}", modelId);
        }
    }

    private string Truncate(string text)
    {
        if (_maxTokens <= 0)
        {
            return text;
        }

        EnsureTruncation();
        return EmbeddingTruncation.TruncateForEmbedding(text, _maxTokens, _tokenizer).Text;
    }

    private string ApplyQueryInstruction(string text) =>
        string.IsNullOrEmpty(_tei.QueryInstruction) ? text : $"{_tei.QueryInstruction}{text}";

    private IReadOnlyList<float> Normalize(IReadOnlyList<float> vector)
    {
        if (!_tei.NormalizeOutput || vector.Count == 0)
        {
            return vector;
        }

        var norm = 0d;
        for (var i = 0; i < vector.Count; i++)
        {
            var value = vector[i];
            norm += value * value;
        }

        norm = Math.Sqrt(norm);
        if (norm <= 0)
        {
            return vector;
        }

        var normalized = new float[vector.Count];
        for (var i = 0; i < vector.Count; i++)
        {
            normalized[i] = (float)(vector[i] / norm);
        }

        return normalized;
    }
}
