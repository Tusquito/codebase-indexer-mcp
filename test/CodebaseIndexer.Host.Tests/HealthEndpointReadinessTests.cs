using System.Net.Http.Json;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Infrastructure.Colbert;
using CodebaseIndexer.Infrastructure.Tei;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Neo4j.Driver;

namespace CodebaseIndexer.Host.Tests;

/// <summary>HTTP readiness/liveness matrix for dependency-aware health (ADR 0031).</summary>
public sealed class HealthEndpointReadinessTests
{
    /// <summary>/health is 200 when TEI is healthy; /alive is always 200.</summary>
    [Fact]
    public async Task Health_ok_when_tei_healthy_alive_always_ok()
    {
        await using var factory = new HealthMatrixFactory(teiHealthy: true);
        var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        health.EnsureSuccessStatusCode();
        var payload = await health.Content.ReadFromJsonAsync<HealthPayload>();
        Assert.Equal("ok", payload!.Status);

        var alive = await client.GetAsync("/alive");
        alive.EnsureSuccessStatusCode();
    }

    /// <summary>/health fails when TEI is down; /alive stays 200.</summary>
    [Fact]
    public async Task Health_fails_when_tei_down_alive_still_ok()
    {
        await using var factory = new HealthMatrixFactory(teiHealthy: false);
        var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        Assert.False(health.IsSuccessStatusCode);

        var alive = await client.GetAsync("/alive");
        alive.EnsureSuccessStatusCode();
    }

    /// <summary>Remote ColBERT readiness fails when sidecar is down (rerank on).</summary>
    [Fact]
    public async Task Health_fails_when_remote_colbert_down()
    {
        await using var factory = new HealthMatrixFactory(
            teiHealthy: true,
            rerankRemote: true,
            colbertHealthy: false);
        var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        Assert.False(health.IsSuccessStatusCode);

        var alive = await client.GetAsync("/alive");
        alive.EnsureSuccessStatusCode();
    }

    /// <summary>Neo4j readiness fails when bolt is down (graph on).</summary>
    [Fact]
    public async Task Health_fails_when_neo4j_down_with_graph_enabled()
    {
        await using var factory = new HealthMatrixFactory(
            teiHealthy: true,
            graphEnabled: true,
            neo4jHealthy: false);
        var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        Assert.False(health.IsSuccessStatusCode);

        var alive = await client.GetAsync("/alive");
        alive.EnsureSuccessStatusCode();
    }

    private sealed record HealthPayload(string Status, string Runtime);

    private sealed class HealthMatrixFactory : McpHostWebApplicationFactory
    {
        private readonly bool _teiHealthy;
        private readonly bool _rerankRemote;
        private readonly bool _colbertHealthy;
        private readonly bool _graphEnabled;
        private readonly bool _neo4jHealthy;

        public HealthMatrixFactory(
            bool teiHealthy,
            bool rerankRemote = false,
            bool colbertHealthy = true,
            bool graphEnabled = false,
            bool neo4jHealthy = true)
            : base(BuildEarlySettings(rerankRemote, graphEnabled))
        {
            _teiHealthy = teiHealthy;
            _rerankRemote = rerankRemote;
            _colbertHealthy = colbertHealthy;
            _graphEnabled = graphEnabled;
            _neo4jHealthy = neo4jHealthy;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITeiEmbeddingsApi>();
                services.AddSingleton<ITeiEmbeddingsApi>(new StubTeiEmbeddingsApi(_teiHealthy));

                if (_rerankRemote)
                {
                    services.RemoveAll<IColbertEmbedApi>();
                    services.AddSingleton<IColbertEmbedApi>(new StubColbertEmbedApi(_colbertHealthy));
                }

                if (_graphEnabled)
                {
                    services.RemoveAll<IDriver>();
                    services.AddSingleton<IDriver>(new StubNeo4jDriver(_neo4jHealthy));
                }
            });
        }

        private static Dictionary<string, string?> BuildEarlySettings(bool rerankRemote, bool graphEnabled)
        {
            var settings = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (rerankRemote)
            {
                settings[$"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.RerankEnabled)}"] = "true";
                settings[$"{ColbertOptions.SectionName}:{nameof(ColbertOptions.EmbedBackend)}"] = "remote";
                settings[$"{IndexingOptions.SectionName}:{nameof(IndexingOptions.UpsertBatch)}"] = "25";
            }

            if (graphEnabled)
            {
                settings[$"{GraphOptions.SectionName}:{nameof(GraphOptions.Enabled)}"] = "true";
                settings[$"{GraphOptions.SectionName}:{nameof(GraphOptions.Neo4jPassword)}"] = "test-pw";
            }

            return settings;
        }
    }
}
