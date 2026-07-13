using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Host;

/// <summary>Preloads embedding models at startup when configured.</summary>
public sealed class IndexPipelineHostedService : BackgroundService
{
    private readonly IIndexEmbeddingService _embedder;
    private readonly IndexingOptions _options;
    private readonly ILogger<IndexPipelineHostedService> _logger;

    /// <summary>Initializes a new instance of the <see cref="IndexPipelineHostedService"/> class.</summary>
    /// <param name="embedder">Embedding service used for model preload.</param>
    /// <param name="options">Indexing configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    public IndexPipelineHostedService(
        IIndexEmbeddingService embedder,
        IOptions<IndexingOptions> options,
        ILogger<IndexPipelineHostedService> logger)
    {
        _embedder = embedder;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PreloadModels)
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
        services.AddSingleton<IIndexPipeline>(sp => sp.GetRequiredService<IIndexCodebaseService>());
        services.AddHostedService<IndexPipelineHostedService>();
        return services;
    }
}
