using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Infrastructure.Indexing;

internal static class ChunkerCore
{
    private static readonly HashSet<string> VerboseLanguages = new(StringComparer.Ordinal)
    {
        "xml", "json", "yaml", "markdown", "protobuf", "sql", "properties", "toml",
        "hcl", "dockerfile", "groovy",
    };

    internal static IReadOnlyList<Chunk> ChunkFile(
        string content,
        string relPath,
        string language,
        string fileSha256,
        int maxChunkLines,
        int chunkOverlapLines,
        double fileMtime,
        Func<LineIndex, string, string, string, int, int, double, IReadOnlyList<Chunk>>? treeSitterChunker)
    {
        var lineIndex = LineIndex.Parse(content);
        if (lineIndex.LineCount == 0
            || (lineIndex.LineCount == 1 && lineIndex.GetLine(0).IsEmpty))
        {
            return Array.Empty<Chunk>();
        }

        var lastLine = lineIndex.LineCount - 1;

        if (VerboseLanguages.Contains(language))
        {
            maxChunkLines = Math.Min(maxChunkLines, 60);
            chunkOverlapLines = Math.Min(chunkOverlapLines, 10);
        }

        IReadOnlyList<SqlProcedureSpan> procedureSpans = Array.Empty<SqlProcedureSpan>();
        IReadOnlyList<Chunk> procedureChunks = Array.Empty<Chunk>();
        if (language == "sql")
        {
            var lines = lineIndex.ToLines();
            procedureChunks = SqlProcedureRegexFallback.ExtractProcedureChunks(
                lines, relPath, language, fileSha256, maxChunkLines, chunkOverlapLines, fileMtime);
            procedureSpans = SqlProcedureRegexFallback.FindProcedureSpans(lines);
        }

        IReadOnlyList<Chunk> chunks;
        if (treeSitterChunker is not null
            && LanguageRegistry.TreeSitterGrammars.ContainsKey(language)
            && LanguageRegistry.ExtractNodeTypes.ContainsKey(language))
        {
            try
            {
                chunks = treeSitterChunker(
                    lineIndex,
                    relPath,
                    language,
                    fileSha256,
                    maxChunkLines,
                    chunkOverlapLines,
                    fileMtime);
            }
            catch (Exception)
            {
                chunks = SlidingWindowRange(lineIndex, 0, lastLine, relPath, language, fileSha256, maxChunkLines, chunkOverlapLines, fileMtime);
            }
        }
        else if (LanguageRegistry.SlidingWindowLanguages.Contains(language)
            || !LanguageRegistry.TreeSitterGrammars.ContainsKey(language))
        {
            chunks = SlidingWindowRange(lineIndex, 0, lastLine, relPath, language, fileSha256, maxChunkLines, chunkOverlapLines, fileMtime);
        }
        else
        {
            chunks = SlidingWindowRange(lineIndex, 0, lastLine, relPath, language, fileSha256, maxChunkLines, chunkOverlapLines, fileMtime);
        }

        if (language == "sql" && procedureSpans.Count > 0)
        {
            chunks = chunks
                .Where(c => !SqlProcedureRegexFallback.LineRangeOverlapsSpans(c.StartLine, c.EndLine, procedureSpans))
                .Concat(procedureChunks)
                .OrderBy(c => c.StartLine)
                .ToArray();
        }
        else if (procedureChunks.Count > 0)
        {
            chunks = chunks.Concat(procedureChunks).OrderBy(c => c.StartLine).ToArray();
        }

        return ApplyFileSymbolType(chunks, relPath, language);
    }

    internal static IReadOnlyList<Chunk> SlidingWindowRange(
        LineIndex lineIndex,
        int start,
        int end,
        string relPath,
        string language,
        string fileSha256,
        int maxLines,
        int overlap,
        double fileMtime,
        string? symbolName = null,
        string symbolType = "other")
    {
        var chunks = new List<Chunk>();
        var pos = start;
        while (pos <= end)
        {
            var chunkEnd = Math.Min(pos + maxLines - 1, end);
            var chunkContent = lineIndex.ExtractRange(pos, chunkEnd);
            chunks.Add(new Chunk(
                ChunkId.FromPathAndLine(relPath, pos + 1),
                relPath,
                chunkContent,
                pos + 1,
                chunkEnd + 1,
                symbolName,
                language,
                fileSha256,
                symbolType)
            {
                Callees = CalleeExtractor.Extract(chunkContent),
            });

            if (chunkEnd >= end)
            {
                break;
            }

            pos += maxLines - overlap;
        }

        return chunks;
    }

    internal static IReadOnlyList<Chunk> SlidingWindowRange(
        IReadOnlyList<string> lines,
        int start,
        int end,
        string relPath,
        string language,
        string fileSha256,
        int maxLines,
        int overlap,
        double fileMtime,
        string? symbolName = null,
        string symbolType = "other") =>
        SlidingWindowRange(LineIndex.Parse(string.Join('\n', lines)), start, end, relPath, language, fileSha256, maxLines, overlap, fileMtime, symbolName, symbolType);

    private static IReadOnlyList<Chunk> ApplyFileSymbolType(IReadOnlyList<Chunk> chunks, string relPath, string language)
    {
        var fileType = ClassifyFileSymbolType(relPath, language);
        if (fileType is null)
        {
            return chunks;
        }

        var defaultName = Path.GetFileName(relPath.Replace('\\', '/'));
        return chunks
            .Select(chunk => chunk with
            {
                SymbolName = chunk.SymbolName ?? defaultName,
                SymbolType = fileType,
            })
            .ToArray();
    }

    private static string? ClassifyFileSymbolType(string relPath, string language)
    {
        var norm = relPath.Replace('\\', '/').ToLowerInvariant();
        var name = Path.GetFileName(norm);
        if (name is "pom.xml" or "package.json" or "go.mod" or "cargo.toml" or "pyproject.toml"
            or "build.gradle" or "build.gradle.kts" or "settings.gradle" or "settings.gradle.kts"
            or "requirements.txt" or "setup.cfg")
        {
            return "manifest";
        }

        if (name is ".env" or ".env.example" or ".env.local"
            || norm.EndsWith(".properties", StringComparison.Ordinal)
            || norm.EndsWith(".ini", StringComparison.Ordinal)
            || norm.EndsWith(".cfg", StringComparison.Ordinal)
            || norm.EndsWith(".toml", StringComparison.Ordinal))
        {
            return "config";
        }

        if (language == "yaml" && (norm.Contains(".github/workflows/", StringComparison.Ordinal)
            || norm.Contains("templates/", StringComparison.Ordinal)))
        {
            return "ops";
        }

        if (language == "yaml")
        {
            return "config";
        }

        return null;
    }
}
