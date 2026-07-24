using System.Net;
using CodebaseIndexer.Infrastructure.Tei;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Refit;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Smoke tests for TeiDenseEmbedder with a stub HTTP handler.</summary>
public sealed class TeiDenseEmbedderSmokeTests
{
    /// <summary>EmbedBatchAsync returns empty for empty input.</summary>
    [Test]
    public async Task EmbedBatch_returns_empty_for_empty_input()
    {
        var handler = new StubTeiHandler();
        var services = new ServiceCollection();
        services.AddSingleton(CreateTeiApi(handler));
        services.AddSingleton(Options.Create(TestSettingsFactory.CreateTeiOptions()));
        services.AddSingleton(Options.Create(TestSettingsFactory.CreateEmbeddingOptions(denseVectorSize: 2)));
        services.AddSingleton(TestSettingsFactory.CreateKnownEmbedModelsOptions());
        services.AddSingleton<TeiDenseEmbedder>();
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        var embedder = provider.GetRequiredService<TeiDenseEmbedder>();
        var result = await embedder.EmbedBatchAsync([]);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEmpty();
    }

    /// <summary>PreloadAsync calls health and embeddings endpoints via Refit.</summary>
    [Test]
    public async Task PreloadAsync_uses_refit_health_and_embeddings()
    {
        var handler = new StubTeiHandler();
        var services = new ServiceCollection();
        services.AddSingleton(CreateTeiApi(handler));
        services.AddSingleton(Options.Create(TestSettingsFactory.CreateTeiOptions()));
        services.AddSingleton(Options.Create(TestSettingsFactory.CreateEmbeddingOptions(denseVectorSize: 2)));
        services.AddSingleton(TestSettingsFactory.CreateKnownEmbedModelsOptions());
        services.AddSingleton<TeiDenseEmbedder>();
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        var embedder = provider.GetRequiredService<TeiDenseEmbedder>();
        await embedder.PreloadAsync();

        await Assert.That(embedder.IsLoaded).IsTrue();
        await Assert.That(handler.HealthCallCount).IsEqualTo(1);
        await Assert.That(handler.EmbeddingsCallCount).IsEqualTo(1);
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