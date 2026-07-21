using System.Text.RegularExpressions;
using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.BuildDeps;

/// <summary>Extracts build dependencies from manifest file content (Python <c>build_deps.py</c>).</summary>
public static partial class BuildDepExtractor
{
    /// <summary>Extract build dependencies from a manifest file's content.</summary>
    public static IReadOnlyList<BuildDep> Extract(string content, string relPath)
    {
        var normalized = relPath.Replace('\\', '/');
        var filename = Path.GetFileName(normalized);
        var suffix = Path.GetExtension(normalized).ToLowerInvariant();

        if (filename.Equals("pom.xml", StringComparison.Ordinal))
        {
            return ExtractMaven(content);
        }

        if (suffix is ".csproj" or ".fsproj" or ".vbproj" or ".nuspec")
        {
            return ExtractNuget(content);
        }

        if (filename.Equals("package.json", StringComparison.Ordinal))
        {
            return ExtractNpm(content);
        }

        if (filename is "build.gradle" or "build.gradle.kts" or "settings.gradle" or "settings.gradle.kts")
        {
            return ExtractGradle(content);
        }

        if (filename.Equals("go.mod", StringComparison.Ordinal))
        {
            return ExtractGo(content);
        }

        if (filename.Equals("Cargo.toml", StringComparison.Ordinal))
        {
            return ExtractCargo(content);
        }

        if (filename is "pyproject.toml" or "requirements.txt" or "setup.cfg")
        {
            return ExtractPython(content, filename);
        }

        return Array.Empty<BuildDep>();
    }

    private static IReadOnlyList<BuildDep> ExtractMaven(string content)
    {
        var deps = new List<BuildDep>();
        foreach (Match blockMatch in MavenDependencyBlockRegex().Matches(content))
        {
            var block = blockMatch.Groups[1].Value;
            var m = MavenArtifactRegex().Match(block);
            if (!m.Success)
            {
                continue;
            }

            var scopeMatch = MavenScopeRegex().Match(block);
            deps.Add(new BuildDep(
                m.Groups[2].Value.Trim(),
                m.Groups[1].Value.Trim(),
                m.Groups[3].Success ? m.Groups[3].Value.Trim() : "",
                scopeMatch.Success ? scopeMatch.Groups[1].Value.Trim() : "compile",
                "maven"));
        }

        var parent = MavenParentRegex().Match(content);
        if (parent.Success)
        {
            deps.Add(new BuildDep(
                parent.Groups[2].Value.Trim(),
                parent.Groups[1].Value.Trim(),
                parent.Groups[3].Success ? parent.Groups[3].Value.Trim() : "",
                "parent",
                "maven"));
        }

        return deps;
    }

    private static IReadOnlyList<BuildDep> ExtractNuget(string content)
    {
        var deps = new List<BuildDep>();
        foreach (Match m in NugetPkgRefRegex().Matches(content))
        {
            deps.Add(new BuildDep(
                m.Groups[1].Value.Trim(),
                Version: m.Groups[2].Success ? m.Groups[2].Value.Trim() : "",
                Ecosystem: "nuget"));
        }

        foreach (Match m in NugetProjRefRegex().Matches(content))
        {
            var path = m.Groups[1].Value.Trim().Replace('\\', '/');
            deps.Add(new BuildDep(
                Path.GetFileNameWithoutExtension(path),
                Scope: "project",
                Ecosystem: "nuget"));
        }

        return deps;
    }

    private static IReadOnlyList<BuildDep> ExtractNpm(string content)
    {
        var deps = new List<BuildDep>();
        foreach (Match sec in NpmDepSectionRegex().Matches(content))
        {
            var scope = sec.Groups[1].Value;
            foreach (Match entry in NpmEntryRegex().Matches(sec.Groups[2].Value))
            {
                deps.Add(new BuildDep(
                    entry.Groups[1].Value.Trim(),
                    Version: entry.Groups[2].Value.Trim(),
                    Scope: scope,
                    Ecosystem: "npm"));
            }
        }

        return deps;
    }

    private static IReadOnlyList<BuildDep> ExtractGradle(string content)
    {
        var deps = new List<BuildDep>();
        foreach (Match m in GradleDepRegex().Matches(content))
        {
            deps.Add(new BuildDep(
                m.Groups[2].Value.Trim(),
                m.Groups[1].Value.Trim(),
                m.Groups[3].Success ? m.Groups[3].Value.Trim() : "",
                Ecosystem: "gradle"));
        }

        foreach (Match m in GradleProjRegex().Matches(content))
        {
            deps.Add(new BuildDep(
                m.Groups[1].Value.Trim().TrimStart(':'),
                Scope: "project",
                Ecosystem: "gradle"));
        }

        return deps;
    }

    private static IReadOnlyList<BuildDep> ExtractGo(string content)
    {
        var deps = new List<BuildDep>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string module, string version)
        {
            if (string.IsNullOrEmpty(module) || !seen.Add(module))
            {
                return;
            }

            var artifact = module.Split('/').Last();
            deps.Add(new BuildDep(artifact, module, version, Ecosystem: "go"));
        }

        foreach (Match block in GoRequireBlockRegex().Matches(content))
        {
            foreach (Match line in GoModuleLineRegex().Matches(block.Groups[1].Value))
            {
                var mod = line.Groups[1].Value.Trim();
                if (mod.StartsWith("//", StringComparison.Ordinal) || mod.StartsWith("/*", StringComparison.Ordinal))
                {
                    continue;
                }

                Add(mod, line.Groups[2].Value.Trim());
            }
        }

        foreach (Match m in GoRequireSingleRegex().Matches(content))
        {
            Add(m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim());
        }

        return deps;
    }

    private static IReadOnlyList<BuildDep> ExtractCargo(string content)
    {
        var deps = new List<BuildDep>();

        void ParseSection(string sectionContent, string scope)
        {
            foreach (Match m in CargoEntryRegex().Matches(sectionContent))
            {
                var version = m.Groups[2].Value.Trim().Trim('"', '\'', '{', '}');
                deps.Add(new BuildDep(m.Groups[1].Value.Trim(), Version: version, Scope: scope, Ecosystem: "cargo"));
            }
        }

        foreach (Match m in CargoDepSectionRegex().Matches(content))
        {
            ParseSection(m.Groups[1].Value, "");
        }

        foreach (Match m in CargoDevSectionRegex().Matches(content))
        {
            ParseSection(m.Groups[1].Value, "dev");
        }

        return deps;
    }

    private static IReadOnlyList<BuildDep> ExtractPython(string content, string filename)
    {
        var deps = new List<BuildDep>();
        if (filename == "pyproject.toml")
        {
            foreach (Match sec in PyprojectDepsRegex().Matches(content))
            {
                foreach (var line in sec.Groups[1].Value.Split('\n'))
                {
                    var trimmed = line.Trim().Trim('"', ',', ' ');
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    {
                        continue;
                    }

                    var m = ReqLineRegex().Match(trimmed);
                    if (m.Success)
                    {
                        deps.Add(new BuildDep(m.Groups[1].Value.Trim(), Ecosystem: "python"));
                    }
                }
            }
        }
        else
        {
            foreach (Match m in ReqLineRegex().Matches(content))
            {
                var name = m.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(name) && !name.StartsWith('#') && !name.StartsWith('-'))
                {
                    deps.Add(new BuildDep(name, Ecosystem: "python"));
                }
            }
        }

        return deps;
    }

    [GeneratedRegex(@"<dependency>(.*?)</dependency>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MavenDependencyBlockRegex();

    [GeneratedRegex(
        @"<groupId>([^<]{1,200})</groupId>\s*<artifactId>([^<]{1,200})</artifactId>(?:\s*<version>([^<]{1,100})</version>)?",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MavenArtifactRegex();

    [GeneratedRegex(@"<scope>([^<]{1,50})</scope>", RegexOptions.CultureInvariant)]
    private static partial Regex MavenScopeRegex();

    [GeneratedRegex(
        @"<parent>.*?<groupId>([^<]{1,200})</groupId>\s*<artifactId>([^<]{1,200})</artifactId>(?:.*?<version>([^<]{1,100})</version>)?",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MavenParentRegex();

    [GeneratedRegex(
        @"<PackageReference\s+Include\s*=\s*[""']([^""']{1,300})[""'](?:[^>]*?Version\s*=\s*[""']([^""']{1,100})[""'])?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NugetPkgRefRegex();

    [GeneratedRegex(
        @"<ProjectReference\s+Include\s*=\s*[""']([^""']{1,300})[""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NugetProjRefRegex();

    [GeneratedRegex(
        @"""(dependencies|devDependencies|peerDependencies)""\s*:\s*\{([^}]{1,4000})\}",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex NpmDepSectionRegex();

    [GeneratedRegex(@"""([^""]{1,200})""\s*:\s*""([^""]{1,100})""", RegexOptions.CultureInvariant)]
    private static partial Regex NpmEntryRegex();

    [GeneratedRegex(
        @"(?:implementation|api|compileOnly|runtimeOnly|testImplementation|testRuntimeOnly|annotationProcessor|kapt|compile)\s*[('""]([\w.\-]+):([\w.\-]+)(?::([\w.\-+]+))?[)'""]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GradleDepRegex();

    [GeneratedRegex(@"project\s*\(\s*[""']:([^""')\s]{1,200})[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GradleProjRegex();

    [GeneratedRegex(@"require\s*\(([^)]{1,5000})\)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex GoRequireBlockRegex();

    [GeneratedRegex(@"^\s*require\s+(\S+)\s+(\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex GoRequireSingleRegex();

    [GeneratedRegex(@"^\s*(\S+)\s+(\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex GoModuleLineRegex();

    [GeneratedRegex(@"\[dependencies\](.*?)(?=\n\s*\[|\z)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CargoDepSectionRegex();

    [GeneratedRegex(@"\[dev-dependencies\](.*?)(?=\n\s*\[|\z)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CargoDevSectionRegex();

    [GeneratedRegex(@"^\s*(\w[\w-]{0,100})\s*=\s*[""']?([^{\n\r\[]{0,100}?)[""']?\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex CargoEntryRegex();

    [GeneratedRegex(@"\[project\].*?dependencies\s*=\s*\[(.*?)\]", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PyprojectDepsRegex();

    [GeneratedRegex(@"^\s*([A-Za-z0-9_\-\.]{1,200})\s*(?:[>=<!~^]{1,2}\s*\S+)?", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ReqLineRegex();
}
