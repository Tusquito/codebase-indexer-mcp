using System.Text.RegularExpressions;
using CodebaseIndexer.Application.BuildDeps;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Models;
using Microsoft.Extensions.Options;

namespace CodebaseIndexer.Application.Search;

/// <summary>Keyword-driven URL/route extraction and reference classification (Python <c>UrlExtractors</c>).</summary>
public sealed partial class UrlExtractors
{
    private IReadOnlyList<Regex> _configExtractors = Array.Empty<Regex>();
    private IReadOnlyList<Regex> _codeExtractors = Array.Empty<Regex>();

    /// <summary>Creates extractors from <see cref="DiscoveryOptions"/> keywords.</summary>
    public UrlExtractors(IOptions<DiscoveryOptions> options)
    {
        var keywords = options.Value.ServiceUrlKeywordList;
        Reconfigure(keywords.Count > 0 ? keywords : DiscoveryOptions.DefaultServiceUrlKeywords
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Creates extractors from an explicit keyword list (tests).</summary>
    public UrlExtractors(IReadOnlyList<string> keywords) => Reconfigure(keywords);

    /// <summary>Recompile config and code URL regexes from a new keyword list.</summary>
    public void Reconfigure(IReadOnlyList<string> keywords)
    {
        var effective = keywords.Count > 0
            ? keywords
            : DiscoveryOptions.DefaultServiceUrlKeywords
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        (_configExtractors, _codeExtractors) = BuildUrlExtractors(effective);
    }

    /// <summary>Extract API route paths from controller/handler definition code.</summary>
    public static IReadOnlyList<string> RoutePaths(string content, string relPath = "")
    {
        var paths = new List<string>();
        foreach (Match m in RouteExtractor1().Matches(content))
        {
            AddRoutePath(paths, m.Groups[1].Value, relPath);
        }

        foreach (Match m in RouteExtractor2().Matches(content))
        {
            AddRoutePath(paths, m.Groups[1].Value, relPath);
        }

        foreach (Match m in RouteExtractor3().Matches(content))
        {
            AddRoutePath(paths, m.Groups[1].Value, relPath);
        }

        foreach (Match m in RouteExtractor4().Matches(content))
        {
            AddRoutePath(paths, m.Groups[1].Value, relPath);
        }

        return paths;
    }

    /// <summary>Extract (paths, base_urls) from config content.</summary>
    public (IReadOnlyList<string> Paths, IReadOnlyList<string> BaseUrls) ConfigUrls(string content)
    {
        var paths = new List<string>();
        var baseUrls = new List<string>();
        foreach (var pattern in _configExtractors)
        {
            foreach (Match m in pattern.Matches(content))
            {
                var val = m.Groups[1].Value;
                if (val.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    baseUrls.Add(val);
                }
                else
                {
                    var path = val.Trim('/');
                    if (path.Length > 2)
                    {
                        paths.Add(path);
                    }
                }
            }
        }

        return (paths, baseUrls);
    }

    /// <summary>Extract URL paths from code string literals.</summary>
    public IReadOnlyList<string> CodeUrls(string content)
    {
        var paths = new List<string>();
        foreach (var pattern in _codeExtractors)
        {
            foreach (Match m in pattern.Matches(content))
            {
                var path = m.Groups[1].Value.Trim('/');
                if (path.Length > 2)
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }

    /// <summary>Classify chunk content as definition, import, http_call, build_dependency, etc.</summary>
    public ReferenceType ClassifyReference(string content, string symbolOrQuery, string relPath = "")
    {
        if (BuildManifestDetector.IsBuildManifest(relPath))
        {
            var deps = BuildDepExtractor.Extract(content, relPath);
            if (deps.Count > 0)
            {
                return ReferenceType.BuildDependency;
            }
        }

        if (ConfigFileRegex().IsMatch(relPath))
        {
            var (paths, baseUrls) = ConfigUrls(content);
            if (baseUrls.Count > 0 || paths.Count > 0)
            {
                return ReferenceType.ServiceConfig;
            }
        }

        if (ClassDefRegex().IsMatch(content)
            && !string.IsNullOrEmpty(symbolOrQuery)
            && Regex.IsMatch(
                content,
                $@"(?:class|interface|enum|record)\s+{Regex.Escape(symbolOrQuery)}\b"))
        {
            return ReferenceType.Definition;
        }

        if (EndpointDefRegex().IsMatch(content))
        {
            return ReferenceType.EndpointDefinition;
        }

        if (HttpCallRegex().IsMatch(content))
        {
            return ReferenceType.HttpCall;
        }

        if (ImportRegex().IsMatch(content))
        {
            return ReferenceType.Import;
        }

        return ReferenceType.Usage;
    }

    /// <summary>Check if a caller path references an endpoint path (segment-aligned).</summary>
    public static bool PathsMatch(string callerPath, string endpointPath)
    {
        static string Norm(string p)
        {
            p = BraceTokenRegex().Replace(p, "");
            return p.Trim('/').ToLowerInvariant();
        }

        var cn = Norm(callerPath);
        var en = Norm(endpointPath);
        if (string.IsNullOrEmpty(cn) || string.IsNullOrEmpty(en))
        {
            return false;
        }

        var cnSegs = cn.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var enSegs = en.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (cnSegs.Length < 2 || enSegs.Length < 2)
        {
            return false;
        }

        var shorter = enSegs.Length <= cnSegs.Length ? enSegs : cnSegs;
        var longer = enSegs.Length <= cnSegs.Length ? cnSegs : enSegs;
        for (var i = 0; i <= longer.Length - shorter.Length; i++)
        {
            var match = true;
            for (var j = 0; j < shorter.Length; j++)
            {
                if (longer[i + j] != shorter[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddRoutePath(List<string> paths, string raw, string relPath)
    {
        var path = raw.Trim('/');
        if (string.IsNullOrEmpty(path) || path.Length <= 1)
        {
            return;
        }

        if (path.Contains("[controller]", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(relPath))
        {
            var fname = Path.GetFileName(relPath.Replace('\\', '/')).Replace(".cs", "", StringComparison.OrdinalIgnoreCase);
            var ctrlName = fname.Replace("Controller", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
            if (!string.IsNullOrEmpty(ctrlName))
            {
                paths.Add(ControllerTokenRegex().Replace(path, ctrlName));
                return;
            }
        }

        paths.Add(path);
    }

    private static (IReadOnlyList<Regex> Config, IReadOnlyList<Regex> Code) BuildUrlExtractors(
        IReadOnlyList<string> keywords)
    {
        var kw = string.Join('|', keywords.Select(Regex.Escape).Append(@"v\d+"));
        IReadOnlyList<Regex> config =
        [
            new Regex($@":\s*(/(?:{kw})/[^\s,}}\]""']{{1,200}})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
            new Regex(@":\s*(/[a-zA-Z][a-zA-Z0-9_-]{0,100}/[a-zA-Z][a-zA-Z0-9_{}/-]{0,200})", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled),
            new Regex(@"(?:host|baseUrl|baseAddress|base-url|base_url|base\.url|endpoint|uri)\s*[:=]\s*[""']?(https?://[^\s""']{1,300})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        ];
        IReadOnlyList<Regex> code =
        [
            new Regex($@"[""'](/(?:{kw})/[^""']{{1,200}})[""']", RegexOptions.CultureInvariant | RegexOptions.Compiled),
            new Regex(@"(?:exchange|getForObject|getForEntity|postForObject|postForEntity|put|delete|retrieve|uri)\s*\(\s*[""']([^""']{0,200}?/[a-zA-Z][a-zA-Z0-9_/-]{1,200})[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
        ];
        return (config, code);
    }

    [GeneratedRegex(@"@(?:Request|Get|Post|Put|Delete|Patch)Mapping\s*\(\s*(?:value\s*=\s*|path\s*=\s*)?[""']([^""']{1,300})[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RouteExtractor1();

    [GeneratedRegex(@"\[(?:Route|Http(?:Get|Post|Put|Delete|Patch))\s*\(\s*[""']([^""']{1,300})[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RouteExtractor2();

    [GeneratedRegex(@"\.Map(?:Get|Post|Put|Delete|Patch)\s*\(\s*[""']([^""']{1,300})[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RouteExtractor3();

    [GeneratedRegex(@"(?:app|router)\.(?:get|post|put|delete|patch)\s*\(\s*[""']([^""']{1,300})[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RouteExtractor4();

    [GeneratedRegex(@"\[controller\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ControllerTokenRegex();

    [GeneratedRegex(@"\{[^}]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex BraceTokenRegex();

    [GeneratedRegex(@"\.(ya?ml|json|properties|env|config)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConfigFileRegex();

    [GeneratedRegex(
        @"^\s*(?:public\s+|private\s+|protected\s+|internal\s+)?(?:abstract\s+|static\s+|sealed\s+|partial\s+)*(?:class|interface|enum|record|struct)\s+",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ClassDefRegex();

    [GeneratedRegex(
        @"@(?:Get|Post|Put|Delete|Patch|Request)Mapping|@(?:GET|POST|PUT|DELETE|PATCH|Path)\b|\[Http(?:Get|Post|Put|Delete|Patch)|\[(?:Route|ApiController)(?:\(|\])|\.Map(?:Get|Post|Put|Delete|Patch)\(|app\.(?:get|post|put|delete|patch)\(|router\.(?:get|post|put|delete)\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EndpointDefRegex();

    [GeneratedRegex(
        @"RestTemplate|WebClient\.(?:create|builder)|\.exchange\(|\.retrieve\(|@FeignClient|HttpClient|IHttpClientFactory|\.GetAsync\(|\.PostAsync\(|\.PutAsync\(|\.DeleteAsync\(|\.SendAsync\(|\.GetFromJsonAsync\(|\.PostAsJsonAsync\(|httpx\.|requests\.|fetch\(|axios\.|\.get\(\s*[""']https?://|\.post\(\s*[""']https?://",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpCallRegex();

    [GeneratedRegex(@"^(?:import\s|from\s|require\(|using\s|#include)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ImportRegex();
}
