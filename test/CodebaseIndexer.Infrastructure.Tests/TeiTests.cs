using System.Net;
using System.Text.Json;
using CodebaseIndexer.Infrastructure.Configuration;
using CodebaseIndexer.Infrastructure.Tei;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Refit;

namespace CodebaseIndexer.Infrastructure.Tests;

public sealed class TeiApiContractTests
{
    [Fact]
    public void EmbeddingsRequest_serializes_openai_shape()
    {
        var json = JsonSerializer.Serialize(new EmbeddingsRequest("model", ["hello"], 768));
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("model", out var model) || document.RootElement.TryGetProperty("Model", out model));
        Assert.Equal("model", model.GetString());
    }

    [Fact]
    public void Refit_client_registers_without_throwing()
    {
        var services = new ServiceCollection();
        services.AddRefitClient<ITeiEmbeddingsApi>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri("http://localhost:8080/"));

        using var provider = services.BuildServiceProvider();
        var api = provider.GetRequiredService<ITeiEmbeddingsApi>();
        Assert.NotNull(api);
    }
}

public sealed class TeiDenseEmbedderSmokeTests
{
    [Fact]
    public async Task EmbedBatch_returns_empty_for_empty_input()
    {
        var handler = new StubTeiHandler();
        var services = new ServiceCollection();
        services.AddSingleton(CreateTeiApi(handler));
        services.AddSingleton(Options.Create(TestSettingsFactory.Create(denseEmbedVectorSize: 2)));
        services.AddSingleton<TeiDenseEmbedder>();
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        var embedder = provider.GetRequiredService<TeiDenseEmbedder>();
        var result = await embedder.EmbedBatchAsync([]);
        Assert.Empty(result);
    }

    [Fact]
    public async Task PreloadAsync_uses_refit_health_and_embeddings()
    {
        var handler = new StubTeiHandler();
        var services = new ServiceCollection();
        services.AddSingleton(CreateTeiApi(handler));
        services.AddSingleton(Options.Create(TestSettingsFactory.Create(denseEmbedVectorSize: 2)));
        services.AddSingleton<TeiDenseEmbedder>();
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        var embedder = provider.GetRequiredService<TeiDenseEmbedder>();
        await embedder.PreloadAsync();

        Assert.True(embedder.IsLoaded);
        Assert.Equal(1, handler.HealthCallCount);
        Assert.Equal(1, handler.EmbeddingsCallCount);
    }

    private static ITeiEmbeddingsApi CreateTeiApi(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://tei/") };
        return RestService.For<ITeiEmbeddingsApi>(client);
    }

    private sealed class StubTeiHandler : HttpMessageHandler
    {
        public int HealthCallCount { get; private set; }
        public int EmbeddingsCallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/health")
            {
                HealthCallCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            EmbeddingsCallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"embedding":[0.1,0.2],"index":0}]}"""),
            });
        }
    }
}
