using System.Text.Json.Serialization;

namespace CodebaseIndexer.Domain.Models;

/// <summary>Closed vocabulary of symbol / chunk kinds stored in payloads and MCP responses.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SymbolType
{
    /// <summary>Free function or equivalent.</summary>
    [JsonStringEnumMemberName("function")]
    Function,

    /// <summary>Class or class-like type declaration.</summary>
    [JsonStringEnumMemberName("class")]
    Class,

    /// <summary>Instance or type method.</summary>
    [JsonStringEnumMemberName("method")]
    Method,

    /// <summary>Fallback when no more specific kind applies.</summary>
    [JsonStringEnumMemberName("other")]
    Other,

    /// <summary>Type / interface / struct / enum declaration.</summary>
    [JsonStringEnumMemberName("type")]
    Type,

    /// <summary>Configuration file or config fragment.</summary>
    [JsonStringEnumMemberName("config")]
    Config,

    /// <summary>Build or package manifest.</summary>
    [JsonStringEnumMemberName("manifest")]
    Manifest,

    /// <summary>Ops / infra script or definition.</summary>
    [JsonStringEnumMemberName("ops")]
    Ops,

    /// <summary>SQL table.</summary>
    [JsonStringEnumMemberName("table")]
    Table,

    /// <summary>SQL procedure / function.</summary>
    [JsonStringEnumMemberName("procedure")]
    Procedure,

    /// <summary>SQL view.</summary>
    [JsonStringEnumMemberName("view")]
    View,

    /// <summary>SQL trigger.</summary>
    [JsonStringEnumMemberName("trigger")]
    Trigger,

    /// <summary>SQL index.</summary>
    [JsonStringEnumMemberName("index")]
    Index,
}
