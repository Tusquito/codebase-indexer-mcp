using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Colbert;

/// <summary>ColBERT embedder that delegates to the HTTP sidecar via Refit.</summary>
public sealed class ColbertRemoteEmbedder : IColbertEmbedder
{
    private readonly IColbertEmbedApi _api;
    private readonly ColbertOptions _options;
    private readonly ILogger<ColbertRemoteEmbedder> _logger;
    private bool _ready;

    /// <summary>Creates a remote ColBERT embedder.</summary>
    public ColbertRemoteEmbedder(
        IColbertEmbedApi api,
        IOptions<ColbertOptions> options,
        ILogger<ColbertRemoteEmbedder> logger)
    {
        _api = api;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public int TokenDimension => KnownColbertModels.ResolveTokenDimension(_options.EmbedModel);

    /// <inheritdoc />
    public bool IsLoaded => _ready;

    /// <inheritdoc />
    public async Task PreloadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await _api.GetHealthAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(health.Model)
                && !string.Equals(health.Model, _options.EmbedModel, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "colbert_sidecar_model_mismatch expected={Expected} sidecar={Sidecar}",
                    _options.EmbedModel,
                    health.Model);
            }

            if (health.TokenDimension is { } sidecarDim && sidecarDim != TokenDimension)
            {
                throw new EmbeddingException(
                    $"ColBERT sidecar token_dimension {sidecarDim} does not match expected {TokenDimension} for model '{_options.EmbedModel}'.");
            }

            var probe = await _api.EmbedColbertAsync(
                new ColbertEmbedRequest(["."]),
                cancellationToken).ConfigureAwait(false);

            if (probe.Embeddings.Count == 0)
            {
                throw new EmbeddingException("ColBERT sidecar probe returned no embeddings.");
            }

            if (probe.TokenDimension != TokenDimension)
            {
                throw new EmbeddingException(
                    $"ColBERT sidecar token_dimension {probe.TokenDimension} does not match expected {TokenDimension}.");
            }

            var first = probe.Embeddings[0];
            if (first.Count > 0 && first[0].Count != TokenDimension)
            {
                throw new EmbeddingException(
                    $"ColBERT sidecar embedding dimension mismatch: expected {TokenDimension}, got {first[0].Count}.");
            }

            _ready = true;
            _logger.LogInformation(
                "colbert_remote_ready model={Model} url={Url}",
                _options.EmbedModel,
                _options.Url);
        }
        catch (EmbeddingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EmbeddingException(
                $"ColBERT sidecar preload failed at {_options.Url}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public void Release() => _ready = false;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return Array.Empty<IReadOnlyList<IReadOnlyList<float>>>();
        }

        var results = new List<IReadOnlyList<IReadOnlyList<float>>>(texts.Count);
        var batchSize = Math.Max(1, _options.EmbedBatchSize);

        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var count = Math.Min(batchSize, texts.Count - offset);
            var batch = new string[count];
            for (var i = 0; i < count; i++)
            {
                batch[i] = texts[offset + i];
            }

            var response = await _api.EmbedColbertAsync(
                new ColbertEmbedRequest(batch),
                cancellationToken).ConfigureAwait(false);

            if (response.Embeddings.Count != count)
            {
                throw new EmbeddingException(
                    $"ColBERT sidecar returned {response.Embeddings.Count} embeddings for {count} inputs.");
            }

            if (response.TokenDimension != TokenDimension)
            {
                throw new EmbeddingException(
                    $"ColBERT sidecar token_dimension {response.TokenDimension} does not match expected {TokenDimension}.");
            }

            results.AddRange(response.Embeddings);
        }

        for (var i = 0; i < results.Count; i++)
        {
            var mv = results[i];
            if (mv.Count > 0 && mv[0].Count != TokenDimension)
            {
                throw new EmbeddingException(
                    $"ColBERT embedding {i} token dimension mismatch: expected {TokenDimension}, got {mv[0].Count}.");
            }
        }

        return results;
    }
}
