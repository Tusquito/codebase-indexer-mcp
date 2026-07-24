using System.Net;
using System.Text.Json;
using CodebaseIndexer.Infrastructure.Tei;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Tests for TEI API request shape and Refit registration.</summary>
public sealed class TeiApiContractTests
{
    /// <summary>EmbeddingsRequest serializes to the OpenAI-compatible shape.</summary>
    [Test]
    public async Task EmbeddingsRequest_serializes_openai_shape()
    {
        var json = JsonSerializer.Serialize(new EmbeddingsRequest("model", ["hello"], 768));
        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.TryGetProperty("model", out var model) || document.RootElement.TryGetProperty("Model", out model)).IsTrue();
        await Assert.That(model.GetString()).IsEqualTo("model");
    }

    /// <summary>Refit client for ITeiEmbeddingsApi registers without throwing.</summary>
    [Test]
    public async Task Refit_client_registers_without_throwing()
    {
        var services = new ServiceCollection();
        services.AddRefitClient<ITeiEmbeddingsApi>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri("http://localhost:8080/"));

        using var provider = services.BuildServiceProvider();
        var api = provider.GetRequiredService<ITeiEmbeddingsApi>();
        await Assert.That(api).IsNotNull();
    }
}