using System.Text.Json.Serialization;

namespace CodebaseIndexer.Domain.Models;

/// <summary>Cross-reference classification kinds (declared for Phase 3 wiring).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReferenceType
{
    /// <summary>Symbol definition site.</summary>
    [JsonStringEnumMemberName("definition")]
    Definition,

    /// <summary>Import / using site.</summary>
    [JsonStringEnumMemberName("import")]
    Import,

    /// <summary>Generic usage site.</summary>
    [JsonStringEnumMemberName("usage")]
    Usage,

    /// <summary>HTTP endpoint definition.</summary>
    [JsonStringEnumMemberName("endpoint_definition")]
    EndpointDefinition,

    /// <summary>Outbound HTTP call site.</summary>
    [JsonStringEnumMemberName("http_call")]
    HttpCall,

    /// <summary>Method / function call site.</summary>
    [JsonStringEnumMemberName("call_site")]
    CallSite,
}
