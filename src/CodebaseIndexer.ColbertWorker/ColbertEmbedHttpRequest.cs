using System.Text.Json.Serialization;

namespace CodebaseIndexer.ColbertWorker;

/// <summary>POST /v1/embed/colbert request body.</summary>
public sealed record ColbertEmbedHttpRequest(
    [property: JsonPropertyName("texts")] IReadOnlyList<string> Texts);
