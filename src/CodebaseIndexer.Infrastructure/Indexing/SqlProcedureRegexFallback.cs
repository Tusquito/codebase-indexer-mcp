using System.Text.RegularExpressions;
using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Infrastructure.Indexing;

internal static partial class SqlProcedureRegexFallback
{
    [GeneratedRegex(
        @"^\s*CREATE\s+(?:OR\s+ALTER\s+)?PROCEDURE\s+(?:\[(?<schema_bracket>[\w]+)\]\.|(?<schema_plain>[\w]+)\.)?\[?(?<name>[\w]+)\]?",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ProcedureStartRegex();

    [GeneratedRegex(@"^BEGIN\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BeginRegex();

    [GeneratedRegex(@"^END\s*;?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EndRegex();

    internal static IReadOnlyList<SqlProcedureSpan> FindProcedureSpans(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return Array.Empty<SqlProcedureSpan>();
        }

        var starts = new List<(int Line, string Name)>();
        foreach (Match match in ProcedureStartRegex().Matches(string.Join('\n', lines)))
        {
            var lineNo = LineNumber(lines, match.Index);
            var name = ParseProcedureName(match);
            if (!string.IsNullOrEmpty(name))
            {
                starts.Add((lineNo, name));
            }
        }

        if (starts.Count == 0)
        {
            return Array.Empty<SqlProcedureSpan>();
        }

        var spans = new List<SqlProcedureSpan>();
        for (var i = 0; i < starts.Count; i++)
        {
            var (startLine, name) = starts[i];
            var endCandidates = new List<int> { lines.Count };
            if (i + 1 < starts.Count)
            {
                endCandidates.Add(starts[i + 1].Line - 1);
            }

            var bodyEnd = FindProcedureBodyEnd(lines, startLine);
            if (bodyEnd is not null)
            {
                endCandidates.Add(bodyEnd.Value);
            }

            var endLine = Math.Max(startLine, endCandidates.Min());
            spans.Add(new SqlProcedureSpan(startLine, endLine, name));
        }

        return spans;
    }

    internal static IReadOnlyList<Chunk> ExtractProcedureChunks(
        IReadOnlyList<string> lines,
        string relPath,
        string language,
        string fileSha256,
        int maxChunkLines,
        int chunkOverlapLines,
        double fileMtime)
    {
        var spans = FindProcedureSpans(lines);
        if (spans.Count == 0)
        {
            return Array.Empty<Chunk>();
        }

        var chunks = new List<Chunk>();
        foreach (var span in spans)
        {
            chunks.AddRange(ChunkerCore.SlidingWindowRange(
                lines,
                span.StartLine - 1,
                span.EndLine - 1,
                relPath,
                language,
                fileSha256,
                maxChunkLines,
                chunkOverlapLines,
                fileMtime,
                span.Name,
                "procedure"));
        }

        return chunks;
    }

    internal static bool LineRangeOverlapsSpans(
        int startLine,
        int endLine,
        IReadOnlyList<SqlProcedureSpan> spans)
    {
        foreach (var span in spans)
        {
            if (startLine <= span.EndLine && span.StartLine <= endLine)
            {
                return true;
            }
        }

        return false;
    }

    private static string ParseProcedureName(Match match)
    {
        var schema = match.Groups["schema_bracket"].Success
            ? match.Groups["schema_bracket"].Value
            : match.Groups["schema_plain"].Value;
        var name = match.Groups["name"].Value;
        return string.IsNullOrEmpty(schema) ? name : $"{schema}.{name}";
    }

    private static int? FindProcedureBodyEnd(IReadOnlyList<string> lines, int startLine)
    {
        var depth = 0;
        var seenBegin = false;
        for (var lineNo = startLine; lineNo <= lines.Count; lineNo++)
        {
            var upper = lines[lineNo - 1].Trim().ToUpperInvariant();
            if (BeginRegex().IsMatch(upper))
            {
                depth++;
                seenBegin = true;
            }
            else if (EndRegex().IsMatch(upper))
            {
                if (seenBegin)
                {
                    depth--;
                    if (depth <= 0)
                    {
                        return lineNo;
                    }
                }
                else
                {
                    return lineNo;
                }
            }
        }

        return null;
    }

    private static int LineNumber(IReadOnlyList<string> lines, int charIndex)
    {
        var text = string.Join('\n', lines);
        var line = 1;
        for (var i = 0; i < charIndex && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
