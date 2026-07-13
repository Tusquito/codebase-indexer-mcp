using System.Text.RegularExpressions;

namespace CodebaseIndexer.Infrastructure.Indexing;

internal sealed partial class GitIgnoreMatcher
{
    private readonly List<Regex> _patterns = [];

    private GitIgnoreMatcher()
    {
    }

    public static GitIgnoreMatcher? Load(string workspaceRoot, string fileName)
    {
        var path = Path.Combine(workspaceRoot, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var lines = File.ReadAllLines(path);
            var matcher = new GitIgnoreMatcher();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                matcher._patterns.Add(CompilePattern(trimmed));
            }

            return matcher._patterns.Count > 0 ? matcher : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool IsIgnored(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(normalized))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex CompilePattern(string pattern)
    {
        var anchored = pattern.StartsWith('/');
        if (anchored)
        {
            pattern = pattern[1..];
        }

        var regex = "^"
            + Regex.Escape(pattern)
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/]*")
                .Replace("\\?", ".")
            + (anchored ? "$" : "(?:/|$)");

        return new Regex(regex, RegexOptions.CultureInvariant);
    }
}
