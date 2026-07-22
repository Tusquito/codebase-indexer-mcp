using System.Collections.Frozen;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TreeSitter;
using DomainSymbolType = CodebaseIndexer.Domain.Models.SymbolType;

namespace CodebaseIndexer.Infrastructure.Indexing;

/// <summary>Tree-sitter AST-based code chunker with sliding-window fallback.</summary>
public sealed class TreeSitterChunker : ICodeChunker
{
    // TreeSitter.DotNet: C# grammar is c-sharp (id "C#"), not "CSharp". No SQL grammar packaged.
    private static readonly FrozenDictionary<string, string> LanguageIds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["python"] = "Python",
        ["javascript"] = "JavaScript",
        ["typescript"] = "TypeScript",
        ["go"] = "Go",
        ["rust"] = "Rust",
        ["java"] = "Java",
        ["c"] = "C",
        ["cpp"] = "Cpp",
        ["csharp"] = "C#",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly ChunkingOptions _options;
    private readonly ILogger<TreeSitterChunker> _logger;
    private readonly Dictionary<string, Parser> _parsers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unavailableLanguageIds = new(StringComparer.Ordinal);

    /// <summary>Creates a chunker from chunking options.</summary>
    /// <param name="options">Chunk line limit and overlap configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public TreeSitterChunker(IOptions<ChunkingOptions> options, ILogger<TreeSitterChunker> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<Chunk> ChunkFile(string relPath, string content, SourceLanguage language, string fileSha256) =>
        ChunkerCore.ChunkFile(
            content,
            relPath,
            language,
            fileSha256,
            _options.MaxLines,
            _options.OverlapLines,
            0,
            TryTreeSitterChunk);

    private IReadOnlyList<Chunk> TryTreeSitterChunk(
        LineIndex lineIndex,
        string relPath,
        SourceLanguage language,
        string fileSha256,
        int maxChunkLines,
        int chunkOverlapLines,
        double fileMtime)
    {
        var lastLine = lineIndex.LineCount - 1;
        IReadOnlyList<Chunk> SlidingFallback() => ChunkerCore.SlidingWindowRange(
            lineIndex, 0, lastLine, relPath, language, fileSha256,
            maxChunkLines, chunkOverlapLines, fileMtime);

        var languageKey = LanguageRegistry.ToRegistryId(language);
        if (!LanguageRegistry.TreeSitterGrammars.ContainsKey(languageKey)
            || !LanguageRegistry.ExtractNodeTypes.TryGetValue(languageKey, out var nodeTypes)
            || !LanguageIds.TryGetValue(languageKey, out var languageId))
        {
            return SlidingFallback();
        }

        try
        {
            var parser = GetOrCreateParser(languageId);
            if (parser is null)
            {
                return SlidingFallback();
            }

            using var tree = parser.Parse(lineIndex.Content);
            if (tree?.RootNode is null)
            {
                return SlidingFallback();
            }

            var chunks = new List<Chunk>();
            ExtractNodes(tree.RootNode, lineIndex, relPath, language, fileSha256, maxChunkLines, nodeTypes, chunks);
            if (chunks.Count == 0)
            {
                return SlidingFallback();
            }

            chunks.Sort(static (left, right) => left.StartLine.CompareTo(right.StartLine));
            var lines = lineIndex.ToLines();
            return ImportHeaderProcessor.ApplyImportHeaders(chunks, tree.RootNode, lines, languageKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "treesitter_parse_failure path={Path}", relPath);
            return SlidingFallback();
        }
    }

    private Parser? GetOrCreateParser(string languageId)
    {
        if (_parsers.TryGetValue(languageId, out var existing))
        {
            return existing;
        }

        if (_unavailableLanguageIds.Contains(languageId))
        {
            return null;
        }

        try
        {
            var language = new Language(languageId);
            var parser = new Parser(language);
            _parsers[languageId] = parser;
            return parser;
        }
        catch (Exception ex)
        {
            _unavailableLanguageIds.Add(languageId);
            _logger.LogWarning(ex, "treesitter_language_unavailable languageId={LanguageId}", languageId);
            return null;
        }
    }

    private static void ExtractNodes(
        Node node,
        LineIndex lineIndex,
        string relPath,
        SourceLanguage language,
        string fileSha256,
        int maxChunkLines,
        FrozenSet<string> nodeTypes,
        List<Chunk> output)
    {
        if (nodeTypes.Contains(node.Type))
        {
            var start = node.StartPosition.Row;
            var end = node.EndPosition.Row;
            if (end - start + 1 > maxChunkLines)
            {
                foreach (var child in node.Children)
                {
                    ExtractNodes(child, lineIndex, relPath, language, fileSha256, maxChunkLines, nodeTypes, output);
                }

                return;
            }

            var nodeContent = node.Text ?? lineIndex.ExtractRange(start, end);
            output.Add(new Chunk(
                ChunkId.FromPathAndLine(relPath, start + 1),
                relPath,
                nodeContent,
                start + 1,
                end + 1,
                ExtractSymbolName(node),
                language,
                fileSha256,
                ClassifySymbolType(node.Type))
            {
                Callees = CalleeExtractor.Extract(nodeContent),
            });
            return;
        }

        foreach (var child in node.Children)
        {
            ExtractNodes(child, lineIndex, relPath, language, fileSha256, maxChunkLines, nodeTypes, output);
        }
    }

    private static string? ExtractSymbolName(Node node)
    {
        foreach (var child in node.NamedChildren)
        {
            if (child.Type is "identifier" or "name" or "property_identifier" or "type_identifier")
            {
                return child.Text;
            }
        }

        return null;
    }

    internal static DomainSymbolType ClassifySymbolType(string nodeType)
    {
        if (nodeType == "create_table")
        {
            return DomainSymbolType.Table;
        }

        if (nodeType == "create_procedure")
        {
            return DomainSymbolType.Procedure;
        }

        if (nodeType == "create_function")
        {
            return DomainSymbolType.Function;
        }

        if (nodeType == "create_view")
        {
            return DomainSymbolType.View;
        }

        if (nodeType == "create_trigger")
        {
            return DomainSymbolType.Trigger;
        }

        if (nodeType == "create_type")
        {
            return DomainSymbolType.Type;
        }

        if (nodeType == "create_index")
        {
            return DomainSymbolType.Index;
        }

        if (nodeType.Contains("class", StringComparison.Ordinal)
            || nodeType.Contains("struct", StringComparison.Ordinal)
            || nodeType.Contains("enum", StringComparison.Ordinal)
            || nodeType.Contains("interface", StringComparison.Ordinal))
        {
            return DomainSymbolType.Class;
        }

        if (nodeType.Contains("method", StringComparison.Ordinal)
            || nodeType.Contains("constructor", StringComparison.Ordinal))
        {
            return DomainSymbolType.Method;
        }

        if (nodeType.Contains("function", StringComparison.Ordinal)
            || nodeType.Contains("arrow_function", StringComparison.Ordinal))
        {
            return DomainSymbolType.Function;
        }

        return DomainSymbolType.Other;
    }
}
