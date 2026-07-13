using System.Collections.Frozen;
using System.Text.RegularExpressions;
using CodebaseIndexer.Domain.Models;
using TreeSitter;

namespace CodebaseIndexer.Infrastructure.Indexing;

internal static partial class ImportHeaderProcessor
{
    private const int MaxImportHeaderLines = 35;

    private static readonly FrozenDictionary<string, FrozenSet<string>> ImportNodeTypes =
        new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
        {
            ["python"] = FrozenSet.ToFrozenSet(
                ["import_statement", "import_from_statement", "future_import_statement"],
                StringComparer.Ordinal),
            ["java"] = FrozenSet.ToFrozenSet(["import_declaration", "package_declaration"], StringComparer.Ordinal),
            ["csharp"] = FrozenSet.ToFrozenSet(["using_directive", "namespace_declaration"], StringComparer.Ordinal),
            ["javascript"] = FrozenSet.ToFrozenSet(["import_statement", "import_declaration"], StringComparer.Ordinal),
            ["typescript"] = FrozenSet.ToFrozenSet(["import_statement", "import_declaration"], StringComparer.Ordinal),
            ["go"] = FrozenSet.ToFrozenSet(["import_declaration", "package_clause"], StringComparer.Ordinal),
            ["rust"] = FrozenSet.ToFrozenSet(
                ["use_declaration", "extern_crate_declaration", "mod_item"],
                StringComparer.Ordinal),
            ["c"] = FrozenSet.ToFrozenSet(["preproc_include", "preproc_def"], StringComparer.Ordinal),
            ["cpp"] = FrozenSet.ToFrozenSet(
                ["preproc_include", "preproc_def", "using_declaration", "namespace_definition"],
                StringComparer.Ordinal),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static IReadOnlyList<Chunk> ApplyImportHeaders(
        IReadOnlyList<Chunk> chunks,
        Node rootNode,
        IReadOnlyList<string> lines,
        string language)
    {
        if (chunks.Count == 0 || !ImportNodeTypes.TryGetValue(language, out var importTypes))
        {
            return chunks;
        }

        var importLines = CollectImportLines(rootNode, lines, importTypes);
        if (importLines.Count == 0)
        {
            return chunks;
        }

        return chunks
            .Select(chunk =>
            {
                var relevant = FilterRelevantImports(importLines, chunk.Content, language);
                return relevant.Count == 0 ? chunk : PrependImportHeader(chunk, string.Join('\n', relevant));
            })
            .ToArray();
    }

    private static List<string> CollectImportLines(Node rootNode, IReadOnlyList<string> lines, FrozenSet<string> importTypes)
    {
        var importLines = new List<string>();
        foreach (var child in rootNode.Children)
        {
            if (!importTypes.Contains(child.Type))
            {
                continue;
            }

            var start = child.StartPosition.Row;
            var end = child.EndPosition.Row;
            for (var i = start; i <= end && i < lines.Count; i++)
            {
                importLines.Add(lines[i]);
            }
        }

        if (importLines.Count > MaxImportHeaderLines)
        {
            importLines = importLines.Take(MaxImportHeaderLines).ToList();
        }

        return importLines;
    }

    private static List<string> FilterRelevantImports(
        IReadOnlyList<string> importLines,
        string chunkContent,
        string language)
    {
        var relevant = new List<string>();
        foreach (var line in importLines)
        {
            var names = ExtractImportedNames(line, language);
            if (names is null)
            {
                relevant.Add(line);
                continue;
            }

            if (names.Count == 0)
            {
                continue;
            }

            if (names.Any(name => SymbolReferencedInContent(name, chunkContent)))
            {
                relevant.Add(line);
            }
        }

        return relevant;
    }

    private static Chunk PrependImportHeader(Chunk chunk, string header)
    {
        var preview = header.Length >= 20 ? header[..20] : header;
        if (!string.IsNullOrEmpty(preview) && chunk.Content.StartsWith(preview, StringComparison.Ordinal))
        {
            return chunk;
        }

        return chunk with { Content = header + "\n" + chunk.Content };
    }

    private static bool SymbolReferencedInContent(string name, string content) =>
        SymbolRegex(name).IsMatch(content);

    private static Regex SymbolRegex(string name) =>
        new($@"(?<![A-Za-z0-9_]){Regex.Escape(name)}(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);

    private static IReadOnlyList<string>? ExtractImportedNames(string line, string language) =>
        language switch
        {
            "python" => ParsePythonImportNames(line),
            "java" => ParseJavaImportNames(line),
            "csharp" => ParseCsharpUsingNames(line),
            "javascript" or "typescript" => ParseJsImportNames(line),
            "go" => ParseGoImportNames(line),
            "rust" => ParseRustUseNames(line),
            "c" or "cpp" => ParseCIncludeNames(line),
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
        if (nsMatch.Success)
        {
            return [nsMatch.Groups[1].Value];
        }

        return names;
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

    private static string LastDottedSegment(string qualified) =>
        qualified.Split('.').Last();

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
