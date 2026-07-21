namespace CodebaseIndexer.Application.Options;

/// <summary>Configuration for cross-ref, service-map, recommend, and outlier discovery.</summary>
public sealed class DiscoveryOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Discovery";

    /// <summary>Default URL-path keywords (Python <c>DEFAULT_SERVICE_URL_KEYWORDS</c>).</summary>
    public const string DefaultServiceUrlKeywords =
        "rest,api,profile,service,internal,public,gateway,graphql,webhook,auth,users,accounts";

    /// <summary>When false, recommend_code and find_outlier_chunks tools are not registered.</summary>
    public bool RecommendEnabled { get; init; }

    /// <summary>Combined positive + negative example cap for recommend_code.</summary>
    public int RecommendMaxExamples { get; init; }

    /// <summary>Max dense vectors sampled as outlier context.</summary>
    public int OutlierMaxContextSamples { get; init; }

    /// <summary>Exclude outlier candidates with cosine similarity above this threshold.</summary>
    public float OutlierMaxSimilarity { get; init; }

    /// <summary>Comma-separated URL path keywords for extractors.</summary>
    public string ServiceUrlKeywords { get; init; } = string.Empty;

    /// <summary>Pipe/newline-separated extra discovery queries for service-map.</summary>
    public string ServiceDiscoveryExtraQueries { get; init; } = string.Empty;

    /// <summary>Parsed non-empty service URL keywords.</summary>
    public IReadOnlyList<string> ServiceUrlKeywordList =>
        ServiceUrlKeywords
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Parsed non-empty extra service-discovery queries.</summary>
    public IReadOnlyList<string> ServiceDiscoveryExtraQueryList
    {
        get
        {
            var raw = ServiceDiscoveryExtraQueries.Replace('|', '\n');
            return raw
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
