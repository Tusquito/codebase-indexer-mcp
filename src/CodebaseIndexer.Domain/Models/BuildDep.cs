namespace CodebaseIndexer.Domain.Models;

/// <summary>One declared dependency extracted from a build manifest file.</summary>
/// <param name="Artifact">Artifact / package / module name.</param>
/// <param name="Group">Group / namespace (Maven groupId, Go module path, …).</param>
/// <param name="Version">Declared version string when present.</param>
/// <param name="Scope">Dependency scope (compile, test, project, …).</param>
/// <param name="Ecosystem">Ecosystem label (maven, nuget, npm, gradle, go, cargo, python).</param>
public sealed record BuildDep(
    string Artifact,
    string Group = "",
    string Version = "",
    string Scope = "",
    string Ecosystem = "");
