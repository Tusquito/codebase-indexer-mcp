using System.Collections.Frozen;

namespace CodebaseIndexer.Application.BuildDeps;

/// <summary>Detects known build manifest file paths.</summary>
public static class BuildManifestDetector
{
    private static readonly FrozenSet<string> ManifestFilenames = FrozenSet.ToFrozenSet(
        [
            "pom.xml",
            "build.gradle", "build.gradle.kts",
            "settings.gradle", "settings.gradle.kts",
            "package.json",
            "go.mod",
            "Cargo.toml",
            "pyproject.toml",
            "requirements.txt",
            "setup.cfg",
        ],
        StringComparer.Ordinal);

    private static readonly string[] ManifestSuffixes = [".csproj", ".fsproj", ".vbproj", ".nuspec"];

    /// <summary>Return true if <paramref name="relPath"/> is a known build manifest file.</summary>
    public static bool IsBuildManifest(string relPath)
    {
        var normalized = relPath.Replace('\\', '/');
        var name = Path.GetFileName(normalized);
        if (ManifestFilenames.Contains(name))
        {
            return true;
        }

        return ManifestSuffixes.Any(suf => normalized.EndsWith(suf, StringComparison.OrdinalIgnoreCase));
    }
}
