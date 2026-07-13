using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Host;

public sealed class IndexPipelineHostedService : BackgroundService
{
    private readonly IIndexEmbeddingService _embedder;
    private readonly IOptions<Settings> _settings;
    private readonly ILogger<IndexPipelineHostedService> _logger;

    public IndexPipelineHostedService(
        IIndexEmbeddingService embedder,
        IOptions<Settings> settings,
        ILogger<IndexPipelineHostedService> logger)
    {
        _embedder = embedder;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Value.PreloadModels)
        {
            return;
        }

        try
        {
            await _embedder.PreloadAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("Index pipeline embedders preloaded");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Index pipeline preload failed — indexing will retry at job start");
        }
    }
}

internal static class IndexingServiceRegistration
{
    public static IServiceCollection AddIndexingServices(this IServiceCollection services)
    {
        services.AddOptions<IndexingOptions>()
            .Configure<IOptions<Settings>>((options, settings) =>
            {
                var s = settings.Value;
                options.HybridSearch = s.HybridSearch;
                options.SequentialEmbed = s.SequentialEmbed;
                options.MemoryPressureWarnPct = s.MemoryPressureWarnPct;
                options.MemoryPressureHaltPct = s.MemoryPressureHaltPct;
                options.ReleaseModelsAfterIndex = s.ReleaseModelsAfterIndex;
                options.WorkspacePath = s.WorkspacePath;
                options.FlushEvery = s.FlushEvery;
                options.UpsertBatch = s.UpsertBatch;
            });

        services.AddSingleton<IIndexPipeline>(sp => sp.GetRequiredService<IIndexCodebaseService>());
        services.AddHostedService<IndexPipelineHostedService>();
        return services;
    }
}
