using CodebaseIndexer.Host;

var builder = WebApplication.CreateSlimBuilder(args);

builder.AddCodebaseIndexerHost();

var app = builder.Build();

app.MapCodebaseIndexerEndpoints();
app.Run();

public partial class Program;
