using System.Text.Json.Serialization;

namespace CodebaseIndexer.Domain.Models;

/// <summary>How a discovery / cross-ref hit was matched.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MatchType
{
    /// <summary>Semantic / vector match.</summary>
    [JsonStringEnumMemberName("semantic")]
    Semantic,

    /// <summary>Exact symbol-name match.</summary>
    [JsonStringEnumMemberName("exact_symbol")]
    ExactSymbol,

    /// <summary>Import-path search match.</summary>
    [JsonStringEnumMemberName("import_search")]
    ImportSearch,

    /// <summary>Callee / call-site payload match.</summary>
    [JsonStringEnumMemberName("call_site")]
    CallSite,
}
