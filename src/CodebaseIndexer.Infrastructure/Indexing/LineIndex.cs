namespace CodebaseIndexer.Infrastructure.Indexing;

/// <summary>Line-offset index over file content; avoids per-chunk Split/Join allocations.</summary>
internal sealed class LineIndex
{
    private readonly int[] _lineStarts;

    private LineIndex(string content, int[] lineStarts)
    {
        Content = content;
        _lineStarts = lineStarts;
    }

    public string Content { get; }

    public int LineCount => _lineStarts.Length;

    public static LineIndex Parse(string content)
    {
        if (content.Length == 0)
        {
            return new LineIndex(content, [0]);
        }

        var newlineCount = 0;
        foreach (var ch in content)
        {
            if (ch == '\n')
            {
                newlineCount++;
            }
        }

        var starts = new int[newlineCount + 1];
        starts[0] = 0;
        var index = 1;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                starts[index++] = i + 1;
            }
        }

        return new LineIndex(content, starts);
    }

    public ReadOnlySpan<char> GetLine(int lineIndex)
    {
        var span = Content.AsSpan();
        var start = _lineStarts[lineIndex];
        var end = lineIndex + 1 < _lineStarts.Length ? _lineStarts[lineIndex + 1] : span.Length;
        var line = span.Slice(start, end - start);
        if (line.Length > 0 && line[^1] == '\r')
        {
            line = line[..^1];
        }

        return line;
    }

    public string ExtractRange(int startLine, int endLineInclusive)
    {
        if (startLine > endLineInclusive)
        {
            return string.Empty;
        }

        var span = Content.AsSpan();
        var startOffset = _lineStarts[startLine];
        var endOffset = endLineInclusive + 1 < _lineStarts.Length
            ? _lineStarts[endLineInclusive + 1]
            : span.Length;

        while (endOffset > startOffset && (span[endOffset - 1] is '\n' or '\r'))
        {
            endOffset--;
        }

        return span.Slice(startOffset, endOffset - startOffset).ToString();
    }

    public string[] ToLines()
    {
        var lines = new string[LineCount];
        for (var i = 0; i < LineCount; i++)
        {
            lines[i] = GetLine(i).ToString();
        }

        return lines;
    }
}
