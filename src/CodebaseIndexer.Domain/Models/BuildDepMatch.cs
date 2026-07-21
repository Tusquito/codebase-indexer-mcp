namespace CodebaseIndexer.Domain.Models;

/// <summary>A build dependency matched to an indexed collection name.</summary>
/// <param name="Artifact">Artifact name from the manifest.</param>
/// <param name="Group">Group / namespace.</param>
/// <param name="Version">Declared version.</param>
/// <param name="Scope">Dependency scope.</param>
/// <param name="Ecosystem">Ecosystem label.</param>
/// <param name="MatchedCollection">Indexed collection name that matched.</param>
/// <param name="MatchConfidence"><c>exact</c> or <c>fuzzy</c>.</param>
public sealed record BuildDepMatch(
    string Artifact,
    string Group,
    string Version,
    string Scope,
    string Ecosystem,
    string MatchedCollection,
    string MatchConfidence);
