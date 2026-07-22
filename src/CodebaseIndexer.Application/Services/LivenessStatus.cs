using System.Text.Json.Serialization;

namespace CodebaseIndexer.Application.Services;

/// <summary>Host liveness JSON status values (declared for Phase 3 wiring).</summary>
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
