namespace CodebaseIndexer.Application.Services;

/// <summary>Health check result for the MCP server.</summary>
/// <param name="Status">Overall health status (e.g. "ok").</param>
/// <param name="Runtime">Runtime identifier (e.g. "dotnet").</param>
public sealed record HealthStatus(string Status, string Runtime);
