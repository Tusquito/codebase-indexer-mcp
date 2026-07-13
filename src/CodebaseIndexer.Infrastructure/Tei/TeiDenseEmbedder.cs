using System.Net.Http.Json;
using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Infrastructure.Tei;

public sealed class TeiDenseEmbedder : IDenseEmbedder
{
    private readonly ITeiEmbeddingsApi _api;
    private readonly Settings _settings;
    private readonly ILogger<TeiDenseEmbedder> _logger;
    private readonly HttpClient _httpClient;
    private bool _ready;

    public TeiDenseEmbedder(
        ITeiEmbeddingsApi api,
        IOptions<Settings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<TeiDenseEmbedder> logger)
    {
        _api = api;
        _settings = settings.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient(nameof(TeiDenseEmbedder));
        _httpClient.BaseAddress = new Uri(_settings.TeiUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TeiTimeoutSeconds);
    }

    public string BackendName => "tei";
    public int VectorSize => _settings.DenseEmbedVectorSize;
    public bool IsLoaded => _ready;

    public async Task PreloadAsync(CancellationToken cancellationToken = default)
    {
        using var healthResponse = await _httpClient.GetAsync("/health", cancellationToken).ConfigureAwait(false);
        healthResponse.EnsureSuccessStatusCode();

        var probe = await _api.CreateEmbeddingsAsync(
            new EmbeddingsRequest(_settings.DenseEmbedModel, ["."], _settings.MrlDimensions),
            cancellationToken).ConfigureAwait(false);

        var embedding = probe.Data.OrderBy(d => d.Index).First().Embedding;
        if (embedding.Count != VectorSize)
        {
            throw new EmbeddingException(
                $"TEI model '{_settings.DenseEmbedModel}' returned dimension {embedding.Count}, expected {VectorSize}.");
        }

        _ready = true;
        _logger.LogInformation("TEI dense embedder ready at {TeiUrl} for model {Model}", _settings.TeiUrl, _settings.DenseEmbedModel);
    }

    public void Release() => _ready = false;

    public Task<IReadOnlyList<IReadOnlyList<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default) =>
        EmbedInternalAsync(texts, isQuery: false, cancellationToken);

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
        var batchSize = Math.Max(1, _settings.TeiEmbedBatchSize);

        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var batch = texts.Skip(offset).Take(batchSize)
                .Select(text => isQuery ? ApplyQueryInstruction(text) : text)
                .ToArray();
            var response = await _api.CreateEmbeddingsAsync(
                new EmbeddingsRequest(_settings.DenseEmbedModel, batch, _settings.MrlDimensions),
                cancellationToken).ConfigureAwait(false);

            var ordered = response.Data.OrderBy(d => d.Index).Select(d => Normalize(d.Embedding)).ToArray();
            if (ordered.Length != batch.Length)
            {
                throw new EmbeddingException($"TEI returned {ordered.Length} embeddings for {batch.Length} inputs.");
            }

            results.AddRange(ordered);
        }

        return results;
    }

    private string ApplyQueryInstruction(string text) =>
        string.IsNullOrEmpty(_settings.QueryInstruction) ? text : $"{_settings.QueryInstruction}{text}";

    private IReadOnlyList<float> Normalize(IReadOnlyList<float> vector)
    {
        if (!_settings.NormalizeOutput || vector.Count == 0)
        {
            return vector;
        }

        var norm = Math.Sqrt(vector.Sum(v => v * v));
        if (norm <= 0)
        {
            return vector;
        }

        return vector.Select(v => (float)(v / norm)).ToArray();
    }
}
