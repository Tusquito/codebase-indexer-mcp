using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.BuildDeps;

/// <summary>Matches extracted build deps against indexed collection names.</summary>
public static class BuildDepCollectionMatcher
{
    private static readonly string[] StripSuffixes =
    [
        "-definitions", "-definition",
        "-api", "-apis",
        "-client", "-clients",
        "-core", "-commons", "-common",
        "-lib", "-library",
        "-impl", "-implementation",
        "-service", "-services",
        "-server",
        "-starter",
    ];

    /// <summary>Match deps to collection names (exact then fuzzy).</summary>
    public static IReadOnlyList<BuildDepMatch> Match(
        IReadOnlyList<BuildDep> deps,
        IReadOnlyList<string> collectionNames,
        string selfCollection = "")
    {
        var candidates = collectionNames.Where(c => c != selfCollection).ToArray();
        var normCandidates = candidates.ToDictionary(c => c, NormalizeName, StringComparer.Ordinal);
        var matches = new List<BuildDepMatch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dep in deps)
        {
            var normArtifact = NormalizeName(dep.Artifact);
            var normGroup = string.IsNullOrEmpty(dep.Group) ? "" : NormalizeName(dep.Group);

            foreach (var (coll, normColl) in normCandidates)
            {
                var matched = false;
                var confidence = "fuzzy";

                if (normArtifact == normColl || (!string.IsNullOrEmpty(normGroup) && normGroup == normColl))
                {
                    matched = true;
                    confidence = "exact";
                }
                else if (normArtifact.Length >= 4
                         && (normColl.Contains(normArtifact, StringComparison.Ordinal)
                             || normArtifact.Contains(normColl, StringComparison.Ordinal)))
                {
                    matched = true;
                }
                else if (!string.IsNullOrEmpty(normGroup) && normGroup.Length >= 4
                         && (normColl.Contains(normGroup, StringComparison.Ordinal)
                             || normGroup.Contains(normColl, StringComparison.Ordinal)))
                {
                    matched = true;
                }

                if (!matched)
                {
                    continue;
                }

                var key = $"{dep.Artifact}:{coll}";
                if (!seen.Add(key))
                {
                    continue;
                }

                matches.Add(new BuildDepMatch(
                    dep.Artifact,
                    dep.Group,
                    dep.Version,
                    dep.Scope,
                    dep.Ecosystem,
                    coll,
                    confidence));
            }
        }

        return matches;
    }

    private static string NormalizeName(string name)
    {
        var n = name.ToLowerInvariant().Replace('_', '-').Replace('.', '-');
        foreach (var suf in StripSuffixes)
        {
            if (n.EndsWith(suf, StringComparison.Ordinal))
            {
                return n[..^suf.Length];
            }
        }

        return n;
    }
}
