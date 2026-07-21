using System.Text.Json.Serialization;

namespace CodebaseIndexer.Infrastructure.Colbert;

/// <summary>POST /v1/embed/colbert request body.</summary>
public sealed record ColbertEmbedRequest(
    [property: JsonPropertyName("texts")] IReadOnlyList<string> Texts);
