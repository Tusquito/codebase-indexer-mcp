using System.Text.RegularExpressions;

namespace CodebaseIndexer.Application.Search;

/// <summary>Minimal reference-type classifier for multi-collection search cross-refs.</summary>
public static partial class ReferenceClassifier
{
    /// <summary>Classify how <paramref name="symbol"/> appears in <paramref name="content"/>.</summary>
    public static string Classify(string content, string symbol)
    {
        if (ClassDefRegex().IsMatch(content)
            && Regex.IsMatch(content, $@"(?:class|interface|enum|record)\s+{Regex.Escape(symbol)}\b"))
        {
            return "definition";
        }

        if (EndpointDefRegex().IsMatch(content))
        {
            return "endpoint_definition";
        }

        if (HttpCallRegex().IsMatch(content))
        {
            return "http_call";
        }

        if (ImportRegex().IsMatch(content))
        {
            return "import";
        }

        return "usage";
    }

    [GeneratedRegex(@"\b(class|interface|enum|record)\s+\w+", RegexOptions.Compiled)]
    private static partial Regex ClassDefRegex();

    [GeneratedRegex(@"@(Get|Post|Put|Delete|Patch|Request)Mapping\b|@(Get|Post|Put|Delete|Patch)\(|\[Http(Get|Post|Put|Delete|Patch)", RegexOptions.Compiled)]
    private static partial Regex EndpointDefRegex();

    [GeneratedRegex(@"\b(HttpClient|RestTemplate|WebClient|fetch\(|axios\.|requests\.(get|post))", RegexOptions.Compiled)]
    private static partial Regex HttpCallRegex();

    [GeneratedRegex(@"^\s*(import |using |from .+ import |require\()", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ImportRegex();
}
