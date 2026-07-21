using System.Collections.Frozen;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TreeSitter;

namespace CodebaseIndexer.Infrastructure.Indexing;

/// <summary>Tree-sitter AST-based code chunker with sliding-window fallback.</summary>
public sealed class TreeSitterChunker : ICodeChunker
{
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
        ["csharp"] = "CSharp",
        ["sql"] = "Sql",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly ChunkingOptions _options;
    private readonly ILogger<TreeSitterChunker> _logger;
    private readonly Dictionary<string, Parser> _parsers = new(StringComparer.Ordinal);

    /// <summary>Creates a chunker from chunking options.</summary>
    /// <param name="options">Chunk line limit and overlap configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public TreeSitterChunker(IOptions<ChunkingOptions> options, ILogger<TreeSitterChunker> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<Chunk> ChunkFile(string relPath, string content, string language, string fileSha256) =>
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
        string language,
        string fileSha256,
        int maxChunkLines,
        int chunkOverlapLines,
        double fileMtime)
    {
        if (!LanguageRegistry.TreeSitterGrammars.ContainsKey(language)
            || !LanguageRegistry.ExtractNodeTypes.TryGetValue(language, out var nodeTypes)
            || !LanguageIds.TryGetValue(language, out var languageId))
        {
            return Array.Empty<Chunk>();
        }

        try
        {
            var parser = GetOrCreateParser(languageId);
            using var tree = parser.Parse(lineIndex.Content);
            if (tree?.RootNode is null)
            {
                return Array.Empty<Chunk>();
            }

            var chunks = new List<Chunk>();
            var lastLine = lineIndex.LineCount - 1;
            ExtractNodes(tree.RootNode, lineIndex, relPath, language, fileSha256, maxChunkLines, nodeTypes, chunks);
            if (chunks.Count == 0)
            {
                return ChunkerCore.SlidingWindowRange(
                    lineIndex, 0, lastLine, relPath, language, fileSha256,
                    maxChunkLines, chunkOverlapLines, fileMtime);
            }

            chunks.Sort(static (left, right) => left.StartLine.CompareTo(right.StartLine));
            var lines = lineIndex.ToLines();
            return ImportHeaderProcessor.ApplyImportHeaders(chunks, tree.RootNode, lines, language);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "treesitter_parse_failure path={Path}", relPath);
            return Array.Empty<Chunk>();
        }
    }

    private Parser GetOrCreateParser(string languageId)
    {
        if (_parsers.TryGetValue(languageId, out var existing))
        {
            return existing;
        }

        var language = new Language(languageId);
        var parser = new Parser(language);
        _parsers[languageId] = parser;
        return parser;
    }

    private static void ExtractNodes(
        Node node,
        LineIndex lineIndex,
        string relPath,
        string language,
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

    internal static string ClassifySymbolType(string nodeType)
    {
        if (nodeType == "create_table")
        {
            return "table";
        }

        if (nodeType == "create_procedure")
        {
            return "procedure";
        }

        if (nodeType == "create_function")
        {
            return "function";
        }

        if (nodeType == "create_view")
        {
            return "view";
        }

        if (nodeType == "create_trigger")
        {
            return "trigger";
        }

        if (nodeType == "create_type")
        {
            return "type";
        }

        if (nodeType == "create_index")
        {
            return "index";
        }

        if (nodeType.Contains("class", StringComparison.Ordinal)
            || nodeType.Contains("struct", StringComparison.Ordinal)
            || nodeType.Contains("enum", StringComparison.Ordinal)
            || nodeType.Contains("interface", StringComparison.Ordinal))
        {
            return "class";
        }

        if (nodeType.Contains("method", StringComparison.Ordinal)
            || nodeType.Contains("constructor", StringComparison.Ordinal))
        {
            return "method";
        }

        if (nodeType.Contains("function", StringComparison.Ordinal)
            || nodeType.Contains("arrow_function", StringComparison.Ordinal))
        {
            return "function";
        }

        return "other";
    }
}
