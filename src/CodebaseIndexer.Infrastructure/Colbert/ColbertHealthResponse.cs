using System.Text.Json.Serialization;

namespace CodebaseIndexer.Infrastructure.Colbert;

/// <summary>GET /health response from the ColBERT worker.</summary>
public sealed record ColbertHealthResponse(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("token_dimension")] int? TokenDimension,
    [property: JsonPropertyName("loaded")] bool? Loaded,
    [property: JsonPropertyName("device")] string? Device,
    [property: JsonPropertyName("execution_providers")] IReadOnlyList<string>? ExecutionProviders,
    [property: JsonPropertyName("cuda_available")] bool? CudaAvailable);
