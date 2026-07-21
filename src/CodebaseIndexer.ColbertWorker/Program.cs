using System.Diagnostics.Metrics;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.ColbertWorker;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Colbert;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services
    .AddOptions<EmbeddingOptions>()
    .BindConfiguration(EmbeddingOptions.SectionName);
builder.Services
    .AddOptions<ColbertOptions>()
    .BindConfiguration(ColbertOptions.SectionName)
    .PostConfigure(options =>
    {
        if (string.IsNullOrWhiteSpace(options.EmbedBackend))
        {
            options.EmbedBackend = "onnx";
        }
    });

builder.Services.AddSingleton<ColbertOnnxEmbedder>();
builder.Services.AddSingleton<IColbertEmbedder>(sp => sp.GetRequiredService<ColbertOnnxEmbedder>());

var metricsEnabled = string.Equals(
    Environment.GetEnvironmentVariable("METRICS_ENABLED"),
    "true",
    StringComparison.OrdinalIgnoreCase)
    || string.Equals(Environment.GetEnvironmentVariable("METRICS_ENABLED"), "1", StringComparison.OrdinalIgnoreCase);

var meter = new Meter("CodebaseIndexer.ColbertWorker");
var embedCounter = meter.CreateCounter<long>("colbert_embed_requests");

var app = builder.Build();

var embedder = app.Services.GetRequiredService<ColbertOnnxEmbedder>();
await embedder.PreloadAsync().ConfigureAwait(false);

app.MapGet("/health", (ColbertOnnxEmbedder onnx, IOptions<ColbertOptions> options) =>
{
    var model = options.Value.EmbedModel;
    return Results.Json(new
    {
        model,
        token_dimension = onnx.TokenDimension,
        loaded = onnx.IsLoaded,
        device = onnx.ActiveDevice,
        execution_providers = onnx.ExecutionProviders,
        cuda_available = onnx.ExecutionProviders.Any(p => p.Contains("CUDA", StringComparison.OrdinalIgnoreCase)),
    });
});

if (metricsEnabled)
{
    app.MapGet("/metrics", () => Results.Text("# ColBERT worker metrics exported via OpenTelemetry\n", "text/plain"));
}

app.MapPost("/v1/embed/colbert", async (ColbertEmbedHttpRequest body, IColbertEmbedder colbert) =>
{
    if (body.Texts is null || body.Texts.Count == 0)
    {
        return Results.BadRequest(new { detail = "texts must be non-empty" });
    }

    try
    {
        var embeddings = await colbert.EmbedBatchAsync(body.Texts).ConfigureAwait(false);
        if (metricsEnabled)
        {
            embedCounter.Add(1, new KeyValuePair<string, object?>("status", "success"));
        }

        return Results.Json(new
        {
            embeddings,
            token_dimension = colbert.TokenDimension,
        });
    }
    catch (Exception ex)
    {
        if (metricsEnabled)
        {
            embedCounter.Add(1, new KeyValuePair<string, object?>("status", "error"));
        }

        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.MapDefaultEndpoints();
await app.RunAsync().ConfigureAwait(false);
