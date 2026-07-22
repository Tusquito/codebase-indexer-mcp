using System.Text.RegularExpressions;
using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Graph;

/// <summary>Extracts imported symbol names from source lines (Python <c>extract_imported_names</c>).</summary>
public static partial class ImportNameExtractor
{
    /// <summary>Collect imported symbol names from raw file content.</summary>
    public static IReadOnlyList<string> ExtractFileImportNames(string content, SourceLanguage language)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var line in content.Split('\n'))
        {
            var imported = ExtractImportedNames(line, language);
            if (imported is null || imported.Count == 0)
            {
                continue;
            }

            foreach (var name in imported)
            {
                if (seen.Add(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    /// <summary>Parse imported names from a single import/using line.</summary>
    public static IReadOnlyList<string>? ExtractImportedNames(string line, SourceLanguage language) =>
        language switch
        {
            SourceLanguage.Python => ParsePythonImportNames(line),
            SourceLanguage.Java => ParseJavaImportNames(line),
            SourceLanguage.CSharp => ParseCsharpUsingNames(line),
            SourceLanguage.JavaScript or SourceLanguage.TypeScript => ParseJsImportNames(line),
            SourceLanguage.Go => ParseGoImportNames(line),
            SourceLanguage.Rust => ParseRustUseNames(line),
            SourceLanguage.C or SourceLanguage.Cpp => ParseCIncludeNames(line),
            _ => Array.Empty<string>(),
        };

    private static IReadOnlyList<string>? ParsePythonImportNames(string line)
    {
        var stripped = line.Trim();
        var importMatch = PythonImportRegex().Match(stripped);
        if (importMatch.Success)
        {
            return [importMatch.Groups[2].Success ? importMatch.Groups[2].Value : LastDottedSegment(importMatch.Groups[1].Value)];
        }

        var fromMatch = PythonFromImportRegex().Match(stripped);
        if (!fromMatch.Success)
        {
            return Array.Empty<string>();
        }

        var tail = fromMatch.Groups[1].Value.Trim();
        if (tail == "*")
        {
            return null;
        }

        return tail.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var alias = PythonAliasRegex().Match(part);
                return alias.Success ? alias.Groups[2].Value : LastDottedSegment(part);
            })
            .ToArray();
    }

    private static IReadOnlyList<string>? ParseJavaImportNames(string line)
    {
        var stripped = line.Trim();
        if (stripped.StartsWith("package ", StringComparison.Ordinal))
        {
            return null;
        }

        var match = JavaImportRegex().Match(stripped);
        if (!match.Success)
        {
            return Array.Empty<string>();
        }

        var qualified = match.Groups[1].Value.Trim();
        return qualified.EndsWith(".*", StringComparison.Ordinal)
            ? null
            : [LastDottedSegment(qualified)];
    }

    private static IReadOnlyList<string>? ParseCsharpUsingNames(string line)
    {
        var stripped = line.Trim();
        if (stripped.StartsWith("namespace ", StringComparison.Ordinal) && !stripped.Contains('{'))
        {
            return null;
        }

        var match = CsharpUsingRegex().Match(stripped);
        if (!match.Success)
        {
            return Array.Empty<string>();
        }

        var qualified = match.Groups[1].Value.Trim();
        return qualified.EndsWith('*')
            ? null
            : [LastDottedSegment(qualified)];
    }

    private static IReadOnlyList<string> ParseJsImportNames(string line)
    {
        var stripped = line.Trim();
        var names = new List<string>();
        var defaultMatch = JsDefaultImportRegex().Match(stripped);
        if (defaultMatch.Success)
        {
            names.Add(defaultMatch.Groups[1].Value);
        }

        var braceMatch = JsBraceImportRegex().Match(stripped);
        if (braceMatch.Success)
        {
            foreach (var part in braceMatch.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var alias = JsAliasRegex().Match(part);
                names.Add(alias.Success ? alias.Groups[2].Value : part.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
            }

            return names;
        }

        var nsMatch = JsNamespaceImportRegex().Match(stripped);
        return nsMatch.Success ? [nsMatch.Groups[1].Value] : names;
    }

    private static IReadOnlyList<string>? ParseGoImportNames(string line)
    {
        var stripped = line.Trim();
        if (stripped.StartsWith("package ", StringComparison.Ordinal))
        {
            return null;
        }

        var simple = GoSimpleImportRegex().Match(stripped);
        if (simple.Success)
        {
            return [GoImportPathName(simple.Groups[1].Value)];
        }

        var grouped = GoGroupedImportRegex().Match(stripped);
        return grouped.Success
            ? [GoImportPathName(grouped.Groups[1].Value)]
            : Array.Empty<string>();
    }

    private static IReadOnlyList<string>? ParseRustUseNames(string line)
    {
        var match = RustUseRegex().Match(line.Trim());
        if (!match.Success)
        {
            return Array.Empty<string>();
        }

        var path = match.Groups[1].Value.Trim();
        if (path.EndsWith("::*", StringComparison.Ordinal) || path == "*")
        {
            return null;
        }

        var alias = RustAliasRegex().Match(path);
        if (alias.Success)
        {
            return [alias.Groups[1].Value];
        }

        var brace = RustBraceRegex().Match(path);
        if (brace.Success)
        {
            return brace.Groups[1].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
        }

        return path.Contains("::", StringComparison.Ordinal)
            ? [LastDottedSegment(path.Replace("::", "."))]
            : [LastDottedSegment(path)];
    }

    private static IReadOnlyList<string> ParseCIncludeNames(string line)
    {
        var stripped = line.Trim();
        var include = CIncludeRegex().Match(stripped);
        if (include.Success)
        {
            var baseName = include.Groups[1].Value.Split('/').Last();
            if (baseName.EndsWith(".h", StringComparison.Ordinal))
            {
                baseName = baseName[..^2];
            }

            return [baseName];
        }

        var define = CDefineRegex().Match(stripped);
        return define.Success ? [define.Groups[1].Value] : Array.Empty<string>();
    }

    private static string LastDottedSegment(ReadOnlySpan<char> qualified)
    {
        var index = qualified.LastIndexOf('.');
        return index < 0 ? qualified.ToString() : qualified[(index + 1)..].ToString();
    }

    private static string GoImportPathName(string importPath)
    {
        var path = importPath.TrimEnd('/');
        return path.Split('/').Last();
    }

    [GeneratedRegex(@"^import\s+([\w.]+)(?:\s+as\s+(\w+))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PythonImportRegex();

    [GeneratedRegex(@"^from\s+[\w.]+\s+import\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex PythonFromImportRegex();

    [GeneratedRegex(@"^([\w.]+)\s+as\s+(\w+)$", RegexOptions.CultureInvariant)]
    private static partial Regex PythonAliasRegex();

    [GeneratedRegex(@"^import\s+(?:static\s+)?(.+?)\s*;\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex JavaImportRegex();

    [GeneratedRegex(@"^using\s+(?:static\s+)?(.+?)\s*;\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CsharpUsingRegex();

    [GeneratedRegex(@"^import\s+(\w+)[\s,]", RegexOptions.CultureInvariant)]
    private static partial Regex JsDefaultImportRegex();

    [GeneratedRegex(@"\{([^}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex JsBraceImportRegex();

    [GeneratedRegex(@"^(\w+)\s+as\s+(\w+)$", RegexOptions.CultureInvariant)]
    private static partial Regex JsAliasRegex();

    [GeneratedRegex(@"^import\s+\*\s+as\s+(\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex JsNamespaceImportRegex();

    [GeneratedRegex(@"^import\s+""([^""]+)""\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex GoSimpleImportRegex();

    [GeneratedRegex(@"^(?:\w+\s+)?""([^""]+)""\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex GoGroupedImportRegex();

    [GeneratedRegex(@"^use\s+(.+);\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex RustUseRegex();

    [GeneratedRegex(@"\s+as\s+(\w+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex RustAliasRegex();

    [GeneratedRegex(@"\{([^}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex RustBraceRegex();

    [GeneratedRegex(@"#include\s+""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex CIncludeRegex();

    [GeneratedRegex(@"#define\s+(\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex CDefineRegex();
}
