using System.Collections.Frozen;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TreeSitter;

namespace CodebaseIndexer.Infrastructure.Indexing;

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

    private readonly Settings _settings;
    private readonly ILogger<TreeSitterChunker> _logger;
    private readonly Dictionary<string, Parser> _parsers = new(StringComparer.Ordinal);

    public TreeSitterChunker(IOptions<Settings> settings, ILogger<TreeSitterChunker> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public IReadOnlyList<Chunk> ChunkFile(string relPath, string content, string language, string fileSha256) =>
        ChunkerCore.ChunkFile(
            content,
            relPath,
            language,
            fileSha256,
            _settings.MaxChunkLines,
            _settings.ChunkOverlapLines,
            0,
            TryTreeSitterChunk);

    private IReadOnlyList<Chunk> TryTreeSitterChunk(
        string content,
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
            using var tree = parser.Parse(content);
            if (tree?.RootNode is null)
            {
                return Array.Empty<Chunk>();
            }

            var lines = content.Split('\n');
            var chunks = new List<Chunk>();
            ExtractNodes(tree.RootNode, lines, relPath, language, fileSha256, maxChunkLines, nodeTypes, chunks);
            if (chunks.Count == 0)
            {
                return ChunkerCore.SlidingWindowRange(
                    lines, 0, lines.Length - 1, relPath, language, fileSha256,
                    maxChunkLines, chunkOverlapLines, fileMtime);
            }

            var ordered = chunks.OrderBy(c => c.StartLine).ToArray();
            return ImportHeaderProcessor.ApplyImportHeaders(ordered, tree.RootNode, lines, language);
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
        IReadOnlyList<string> lines,
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
                    ExtractNodes(child, lines, relPath, language, fileSha256, maxChunkLines, nodeTypes, output);
                }

                return;
            }

            var nodeContent = node.Text ?? string.Join('\n', lines.Skip(start).Take(end - start + 1));
            output.Add(new Chunk(
                ChunkId.FromPathAndLine(relPath, start + 1),
                relPath,
                nodeContent,
                start + 1,
                end + 1,
                ExtractSymbolName(node),
                language,
                fileSha256));
            return;
        }

        foreach (var child in node.Children)
        {
            ExtractNodes(child, lines, relPath, language, fileSha256, maxChunkLines, nodeTypes, output);
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
}
