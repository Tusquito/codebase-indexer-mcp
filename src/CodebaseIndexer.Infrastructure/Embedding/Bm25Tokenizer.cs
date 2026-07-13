using System.Globalization;
using System.Text;

namespace CodebaseIndexer.Infrastructure.Embedding;

internal static class Bm25Tokenizer
{
    public static string RemoveNonAlphanumeric(string text) =>
        RemoveNonAlphanumeric(text.AsSpan());

    public static string RemoveNonAlphanumeric(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return string.Empty;
        }

        var buffer = new char[text.Length];
        var length = 0;
        foreach (var ch in text)
        {
            if (IsTokenChar(ch))
            {
                buffer[length++] = char.ToLowerInvariant(ch);
            }
            else if (length > 0 && buffer[length - 1] != ' ')
            {
                buffer[length++] = ' ';
            }
        }

        while (length > 0 && buffer[length - 1] == ' ')
        {
            length--;
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
    }

    public static IReadOnlyList<string> Tokenize(string text) =>
        Tokenize(text.AsSpan());

    public static IReadOnlyList<string> Tokenize(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        Span<char> buffer = stackalloc char[text.Length];
        var length = 0;
        foreach (var ch in text)
        {
            if (IsTokenChar(ch))
            {
                buffer[length++] = char.ToLowerInvariant(ch);
            }
            else if (length > 0)
            {
                tokens.Add(new string(buffer[..length]));
                length = 0;
            }
        }

        if (length > 0)
        {
            tokens.Add(new string(buffer[..length]));
        }

        return tokens;
    }

    private static bool IsTokenChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch is '_' or '$';
}
