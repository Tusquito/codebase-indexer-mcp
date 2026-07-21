using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Host;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateSlimBuilder(args);

builder.AddCodebaseIndexerHost();

var app = builder.Build();

var graph = app.Services.GetRequiredService<IOptions<GraphOptions>>().Value;
var graphLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Graph");
if (graph.Enabled)
{
    graphLogger.LogInformation(
        "graph_enabled uri={Uri} database={Database}",
        graph.Neo4jUri,
        graph.Neo4jDatabase);
}
else
{
    graphLogger.LogInformation("graph_disabled");
}

app.MapCodebaseIndexerEndpoints();
app.Run();

/// <summary>Entry point for the MCP host web application.</summary>
public partial class Program;
