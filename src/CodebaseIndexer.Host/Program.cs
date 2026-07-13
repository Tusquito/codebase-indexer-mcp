using CodebaseIndexer.Application;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Infrastructure;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services
    .AddCodebaseIndexerApplication()
    .AddCodebaseIndexerInfrastructure()
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcp("/mcp");
app.MapGet("/health", async (IHealthService health, CancellationToken cancellationToken) =>
{
    var status = await health.GetStatusAsync(cancellationToken).ConfigureAwait(false);
    return Results.Ok(status);
});

app.Run();

public partial class Program;
