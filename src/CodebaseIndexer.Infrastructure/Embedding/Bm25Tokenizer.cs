using System.Globalization;
using System.Text.RegularExpressions;

namespace CodebaseIndexer.Infrastructure.Embedding;

internal static partial class Bm25Tokenizer
{
    [GeneratedRegex(@"[^\w]", RegexOptions.CultureInvariant)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    public static string RemoveNonAlphanumeric(string text) =>
        Regex.Replace(text, @"[^\w\s]", " ", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Tokenize(string text)
    {
        text = NonWordRegex().Replace(text.ToLowerInvariant(), " ");
        text = WhitespaceRegex().Replace(text, " ");
        text = text.Trim();
        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
