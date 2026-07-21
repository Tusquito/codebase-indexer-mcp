namespace CodebaseIndexer.Domain.Models;

/// <summary>BUILD_DEPENDS artifact row.</summary>
/// <param name="Key">Stable artifact key.</param>
/// <param name="Name">Artifact name.</param>
/// <param name="Group">Group / namespace.</param>
/// <param name="Ecosystem">Ecosystem label.</param>
/// <param name="Version">Declared version.</param>
/// <param name="Scope">Dependency scope.</param>
public sealed record GraphBuildDepRow(
    string Key,
    string Name,
    string Group,
    string Ecosystem,
    string Version,
    string Scope);
