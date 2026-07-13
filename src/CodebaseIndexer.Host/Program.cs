using CodebaseIndexer.Host;

var builder = WebApplication.CreateSlimBuilder(args);

builder.AddCodebaseIndexerHost();

var app = builder.Build();

app.MapCodebaseIndexerEndpoints();
app.Run();

/// <summary>Entry point for the MCP host web application.</summary>
public partial class Program;
