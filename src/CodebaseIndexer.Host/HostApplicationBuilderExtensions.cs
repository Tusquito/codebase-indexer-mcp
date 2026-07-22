using CodebaseIndexer.Application;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Host.Health;
using CodebaseIndexer.Host.Tools;
using CodebaseIndexer.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;

namespace CodebaseIndexer.Host;

/// <summary>Registers MCP host services, health checks, and infrastructure.</summary>
public static class HostApplicationBuilderExtensions
{
    /// <summary>Configures the codebase indexer MCP host application.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddCodebaseIndexerHost(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();
        builder.Services
            .AddCodebaseIndexerApplication()
            .AddCodebaseIndexerInfrastructure()
            .AddIndexingServices()
            .AddScheduledReindex(builder.Configuration)
            .AddHealthChecks()
            .AddCheck<McpHostHealthCheck>("codebase-indexer", tags: ["live"])
            .AddCheck<TeiHealthCheck>("tei", tags: ["ready"]);

        var rerankEnabled = builder.Configuration
            .GetSection(EmbeddingOptions.SectionName)
            .GetValue(nameof(EmbeddingOptions.RerankEnabled), false);
        var colbertBackend = builder.Configuration
            .GetSection(ColbertOptions.SectionName)
            .GetValue<string>(nameof(ColbertOptions.EmbedBackend)) ?? string.Empty;
        if (rerankEnabled
            && (string.IsNullOrWhiteSpace(colbertBackend)
                || string.Equals(colbertBackend, "remote", StringComparison.OrdinalIgnoreCase)))
        {
            builder.Services.AddHealthChecks()
                .AddCheck<ColbertRemoteHealthCheck>("colbert", tags: ["ready"]);
        }

        var graphEnabled = builder.Configuration
            .GetSection(GraphOptions.SectionName)
            .GetValue(nameof(GraphOptions.Enabled), false);
        if (graphEnabled)
        {
            builder.Services.AddHealthChecks()
                .AddCheck<Neo4jHealthCheck>("neo4j", tags: ["ready"]);
        }

        var mcp = builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<HealthTools>()
            .WithTools<IndexTools>()
            .WithTools<SearchTools>()
            .WithTools<ChunkTools>()
            .WithTools<OutlineTools>()
            .WithTools<SummaryTools>()
            .WithTools<CollectionsTools>()
            .WithTools<CrossReferenceTools>()
            .WithTools<ServiceMapTools>();

        // Resolve RecommendEnabled after options bind; gate tool registration like Python main.py.
        var recommendEnabled = builder.Configuration
            .GetSection(DiscoveryOptions.SectionName)
            .GetValue(nameof(DiscoveryOptions.RecommendEnabled), true);
        if (recommendEnabled)
        {
            mcp.WithTools<RecommendTools>()
                .WithTools<OutlierTools>();
        }

        if (graphEnabled)
        {
            mcp.WithTools<ExpandSearchContextTools>();
        }

        return builder;
    }

    private static IServiceCollection AddScheduledReindex(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(ReindexOptions.SectionName);
        var enabled = section.GetValue(nameof(ReindexOptions.Enabled), true);
        if (!enabled)
        {
            return services;
        }

        var cron = section.GetValue<string>(nameof(ReindexOptions.Cron)) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cron))
        {
            services.AddQuartz(q =>
            {
                var jobKey = new JobKey("scheduled-reindex");
                q.AddJob<ScheduledReindexQuartzJob>(opts => opts.WithIdentity(jobKey));
                q.AddTrigger(opts => opts
                    .ForJob(jobKey)
                    .WithIdentity("scheduled-reindex-trigger")
                    .WithCronSchedule(ToQuartzCron(cron)));
            });
            services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
        }
        else
        {
            services.AddHostedService<ScheduledReindexIntervalHostedService>();
        }

        return services;
    }

    /// <summary>
    /// Maps 5-field unix cron (min hour dom month dow) to Quartz 6-field
    /// (sec min hour dom month dow). Quartz forbids <c>*</c> on both DOM and DOW —
    /// use <c>?</c> for the unspecified field.
    /// </summary>
    internal static string ToQuartzCron(string cron)
    {
        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 5)
        {
            var min = parts[0];
            var hour = parts[1];
            var dom = parts[2];
            var month = parts[3];
            var dow = parts[4];
            if (dom == "*" && dow == "*")
            {
                return $"0 {min} {hour} ? {month} *";
            }

            if (dom == "*")
            {
                return $"0 {min} {hour} ? {month} {dow}";
            }

            if (dow == "*")
            {
                return $"0 {min} {hour} {dom} {month} ?";
            }

            return $"0 {min} {hour} {dom} {month} {dow}";
        }

        if (parts.Length == 6 && parts[3] == "*" && parts[5] == "*")
        {
            return $"{parts[0]} {parts[1]} {parts[2]} ? {parts[4]} *";
        }

        return cron;
    }
}
