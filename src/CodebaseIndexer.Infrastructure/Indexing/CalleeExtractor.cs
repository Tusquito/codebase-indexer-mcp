using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace CodebaseIndexer.Infrastructure.Indexing;

/// <summary>Extracts call-expression tokens for Path D callees payload (Python <c>_extract_callees</c>).</summary>
public static partial class CalleeExtractor
{
    private static readonly FrozenSet<string> Keywords = FrozenSet.ToFrozenSet(
        ["if", "for", "while", "switch", "catch", "return", "new", "synchronized", "super", "this", "do", "else", "try"],
        StringComparer.Ordinal);

    /// <summary>Extract unique callee tokens from chunk source (before import headers).</summary>
    public static IReadOnlyList<string> Extract(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var callees = new List<string>();

        void Add(string token)
        {
            if (seen.Add(token))
            {
                callees.Add(token);
            }
        }

        foreach (Match match in ReceiverMethodCallRegex().Matches(content))
        {
            var receiver = match.Groups[1].Value;
            var method = match.Groups[2].Value;
            if (!Keywords.Contains(method))
            {
                Add(method);
            }

            if (!Keywords.Contains(receiver) && !Keywords.Contains(method))
            {
                Add($"{receiver}.{method}");
            }
        }

        foreach (Match match in BareCallRegex().Matches(content))
        {
            var name = match.Groups[1].Value;
            if (!Keywords.Contains(name))
            {
                Add(name);
            }
        }

        return callees;
    }

    [GeneratedRegex(@"([A-Za-z_][\w$]*)\s*\.\s*([A-Za-z_][\w$]*)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex ReceiverMethodCallRegex();

    [GeneratedRegex(@"\b([A-Za-z_][\w$]*)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex BareCallRegex();
}
