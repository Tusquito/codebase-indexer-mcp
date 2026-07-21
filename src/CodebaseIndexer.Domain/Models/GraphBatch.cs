namespace CodebaseIndexer.Domain.Models;

/// <summary>Structured graph upsert payload for one flush batch.</summary>
public sealed class GraphBatch
{
    /// <summary>Creates an empty batch for <paramref name="collection"/>.</summary>
    public GraphBatch(string collection) => Collection = collection;

    /// <summary>Owning collection name.</summary>
    public string Collection { get; }

    /// <summary>File nodes.</summary>
    public List<GraphFileRow> Files { get; } = [];

    /// <summary>Chunk nodes.</summary>
    public List<GraphChunkRow> Chunks { get; } = [];

    /// <summary>DEFINES edges.</summary>
    public List<GraphDefineRow> Defines { get; } = [];

    /// <summary>CALLS edges.</summary>
    public List<GraphCallRow> Calls { get; } = [];

    /// <summary>IMPORTS edges.</summary>
    public List<GraphImportRow> Imports { get; } = [];

    /// <summary>Endpoint nodes.</summary>
    public List<GraphEndpointRow> Endpoints { get; } = [];

    /// <summary>DECLARES_ENDPOINT edges.</summary>
    public List<GraphDeclaresEndpointRow> DeclaresEndpoint { get; } = [];

    /// <summary>HTTP_CALLS edges.</summary>
    public List<GraphHttpCallRow> HttpCalls { get; } = [];

    /// <summary>CONFIGURES edges.</summary>
    public List<GraphConfiguresRow> Configures { get; } = [];

    /// <summary>BUILD_DEPENDS artifacts.</summary>
    public List<GraphBuildDepRow> BuildDeps { get; } = [];

    /// <summary>RESOLVES_TO edges.</summary>
    public List<GraphResolvesToRow> ResolvesTo { get; } = [];
}
