using System.Text.Json.Serialization;

namespace CodebaseIndexer.Domain.Models;

/// <summary>Host liveness JSON status values.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LivenessStatus
{
    /// <summary>Host is healthy.</summary>
    [JsonStringEnumMemberName("ok")]
    Ok,

    /// <summary>Host is unhealthy.</summary>
    [JsonStringEnumMemberName("unhealthy")]
    Unhealthy,
}
