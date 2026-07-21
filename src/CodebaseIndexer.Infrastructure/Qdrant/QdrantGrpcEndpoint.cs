namespace CodebaseIndexer.Infrastructure.Qdrant;

/// <summary>
/// Parses <see cref="Configuration.QdrantOptions.Url"/> for <c>Qdrant.Client</c> (gRPC only).
/// Qdrant REST defaults to 6333; gRPC defaults to 6334 — using REST as gRPC yields PROTOCOL_ERROR.
/// </summary>
internal static class QdrantGrpcEndpoint
{
    /// <summary>Qdrant default REST/HTTP API port.</summary>
    public const int DefaultRestPort = 6333;

    /// <summary>Qdrant default gRPC port (used by <c>Qdrant.Client</c>).</summary>
    public const int DefaultGrpcPort = 6334;

    /// <summary>
    /// Resolves host, gRPC port, and TLS flag from a Qdrant URL.
    /// Maps well-known REST port <see cref="DefaultRestPort"/> to <see cref="DefaultGrpcPort"/>.
    /// </summary>
    /// <exception cref="ArgumentException">When <paramref name="url"/> is not an absolute URI with a host.</exception>
    public static (string Host, int Port, bool Https) Parse(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException($"Invalid Qdrant URL '{url}'.", nameof(url));
        }

        var port = uri.IsDefaultPort ? DefaultGrpcPort : uri.Port;
        if (port == DefaultRestPort)
        {
            port = DefaultGrpcPort;
        }

        var https = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        return (uri.Host, port, https);
    }
}
