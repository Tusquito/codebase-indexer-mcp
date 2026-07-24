using System.Text.Json.Serialization;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Application.Models;

/// <summary>Failure body nested under <see cref="McpErrorEnvelope"/>.</summary>
/// <remarks>
/// <see cref="Kind"/> serializes as PascalCase enum name (e.g. <c>Validation</c>), not lowercase JSON.
/// </remarks>
public sealed record McpErrorBody(
    [property: JsonPropertyName("kind")]
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    ErrorKind Kind,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("metadata")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string>? Metadata = null);
