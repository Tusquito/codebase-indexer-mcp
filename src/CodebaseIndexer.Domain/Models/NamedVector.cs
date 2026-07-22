using System.Text.Json.Serialization;

namespace CodebaseIndexer.Domain.Models;

/// <summary>Qdrant named-vector identifiers (declared for Phase 2 adoption).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NamedVector
{
    /// <summary>Dense embedding vector.</summary>
    [JsonStringEnumMemberName("dense")]
    Dense,

    /// <summary>Sparse embedding vector.</summary>
    [JsonStringEnumMemberName("sparse")]
    Sparse,

    /// <summary>ColBERT multi-vector.</summary>
    [JsonStringEnumMemberName("colbert")]
    Colbert,
}
