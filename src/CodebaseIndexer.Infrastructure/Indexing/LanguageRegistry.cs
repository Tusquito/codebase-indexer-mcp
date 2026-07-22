using System.Collections.Frozen;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Serialization;

namespace CodebaseIndexer.Infrastructure.Indexing;

internal static class LanguageRegistry
{
    internal sealed record LanguageSpec(
        string Name,
        FrozenSet<string> Extensions,
        FrozenSet<string>? NodeTypes = null,
        string? TreeSitterGrammar = null);

    internal static readonly FrozenSet<string> SlidingWindowLanguages = new[]
    {
        "xml", "yaml", "json", "properties", "toml", "hcl", "dockerfile", "groovy",
        "protobuf", "kotlin", "scala", "ruby", "php", "swift", "dart", "bash",
        "powershell", "markdown",
    }.ToFrozenSet(StringComparer.Ordinal);

    internal static readonly FrozenDictionary<string, string> ExtensionLanguageMap;
    internal static readonly FrozenDictionary<string, string> FilenameLanguageMap;
    internal static readonly FrozenDictionary<string, FrozenSet<string>> ExtractNodeTypes;
    internal static readonly FrozenDictionary<string, string> TreeSitterGrammars;

    static LanguageRegistry()
    {
        var specs = new List<LanguageSpec>
        {
            new("python", [".py"], ["function_definition", "class_definition", "decorated_definition"], "python"),
            new("javascript", [".js", ".jsx"], ["function_declaration", "class_declaration", "arrow_function", "method_definition", "export_statement"], "javascript"),
            new("typescript", [".ts", ".tsx"], ["function_declaration", "class_declaration", "arrow_function", "method_definition", "export_statement"], "typescript"),
            new("go", [".go"], ["function_declaration", "method_declaration", "type_declaration"], "go"),
            new("rust", [".rs"], ["function_item", "impl_item", "struct_item", "enum_item", "trait_item"], "rust"),
            new("java", [".java"], ["class_declaration", "method_declaration", "interface_declaration"], "java"),
            new("c", [".c", ".h"], ["function_definition", "class_specifier", "struct_specifier"], "c"),
            new("cpp", [".cpp", ".cc", ".cxx", ".hpp"], ["function_definition", "class_specifier", "struct_specifier"], "cpp"),
            new("csharp", [".cs"], ["class_declaration", "method_declaration", "interface_declaration", "struct_declaration", "enum_declaration", "namespace_declaration", "constructor_declaration"], "c_sharp"),
            new("sql", [".sql"], ["create_table", "create_function", "create_view", "create_type", "create_trigger", "create_index"], "sql"),
            new("xml", [".xml", ".xsd", ".xsl", ".xslt", ".wsdl", ".csproj", ".fsproj", ".vbproj", ".nuspec"]),
            new("yaml", [".yml", ".yaml"]),
            new("json", [".json"]),
            new("properties", [".properties", ".ini", ".cfg"]),
            new("toml", [".toml"]),
            new("hcl", [".tf", ".hcl"]),
            new("dockerfile", [".dockerfile"]),
            new("groovy", [".groovy", ".gvy", ".gradle"]),
            new("protobuf", [".proto"]),
            new("kotlin", [".kt", ".kts"]),
            new("scala", [".scala"]),
            new("ruby", [".rb"]),
            new("php", [".php"]),
            new("swift", [".swift"]),
            new("dart", [".dart"]),
            new("bash", [".sh", ".bash"]),
            new("powershell", [".ps1"]),
            new("markdown", [".md"]),
        };

        var extMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
        {
            foreach (var ext in spec.Extensions)
            {
                extMap[ext.ToLowerInvariant()] = spec.Name;
            }
        }

        ExtensionLanguageMap = extMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        FilenameLanguageMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Dockerfile"] = "dockerfile",
            ["Jenkinsfile"] = "groovy",
            ["docker-compose.yml"] = "yaml",
            ["docker-compose.yaml"] = "yaml",
            [".env.example"] = "properties",
            [".env.local"] = "properties",
            [".env"] = "properties",
            ["build.gradle.kts"] = "groovy",
            ["settings.gradle.kts"] = "groovy",
        }.ToFrozenDictionary(StringComparer.Ordinal);

        var nodeTypes = new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal);
        var grammars = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            if (spec.NodeTypes is not null)
            {
                nodeTypes[spec.Name] = spec.NodeTypes;
            }

            if (spec.TreeSitterGrammar is not null)
            {
                grammars[spec.Name] = spec.TreeSitterGrammar;
            }
        }

        ExtractNodeTypes = nodeTypes.ToFrozenDictionary(StringComparer.Ordinal);
        TreeSitterGrammars = grammars.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Detects language from filename / extensions; returns null when unrecognized.</summary>
    internal static SourceLanguage? DetectLanguage(string fileName, IReadOnlyList<string> suffixes)
    {
        string? wire = null;
        if (FilenameLanguageMap.TryGetValue(fileName, out var byName))
        {
            wire = byName;
        }
        else if (suffixes.Count > 0)
        {
            var compound = string.Concat(suffixes).ToLowerInvariant();
            if (ExtensionLanguageMap.TryGetValue(compound, out var byCompound))
            {
                wire = byCompound;
            }
            else
            {
                var last = suffixes[^1].ToLowerInvariant();
                if (ExtensionLanguageMap.TryGetValue(last, out var byLast))
                {
                    wire = byLast;
                }
            }
        }

        return wire is not null && DomainEnumWire.TryParse(wire, out SourceLanguage language)
            ? language
            : null;
    }

    /// <summary>Wire id used by internal registry maps.</summary>
    internal static string ToRegistryId(SourceLanguage language) => DomainEnumWire.ToWire(language);
}
