using CodebaseIndexer.Infrastructure.Qdrant;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Tests for Qdrant gRPC URL/port parsing used by <c>Qdrant.Client</c>.</summary>
public sealed class QdrantGrpcEndpointTests
{
    /// <summary>Explicit gRPC port is preserved.</summary>
    [Fact]
    public void Parse_preserves_grpc_port_6334()
    {
        var (host, port, https) = QdrantGrpcEndpoint.Parse("http://qdrant:6334");

        Assert.Equal("qdrant", host);
        Assert.Equal(QdrantGrpcEndpoint.DefaultGrpcPort, port);
        Assert.False(https);
    }

    /// <summary>Well-known REST port is remapped to gRPC (avoids PROTOCOL_ERROR).</summary>
    [Fact]
    public void Parse_remaps_rest_port_6333_to_grpc_6334()
    {
        var (host, port, https) = QdrantGrpcEndpoint.Parse("http://qdrant:6333");

        Assert.Equal("qdrant", host);
        Assert.Equal(QdrantGrpcEndpoint.DefaultGrpcPort, port);
        Assert.False(https);
    }

    /// <summary>Scheme without an explicit port uses the Qdrant gRPC default.</summary>
    [Fact]
    public void Parse_default_http_port_uses_grpc_default()
    {
        var (host, port, https) = QdrantGrpcEndpoint.Parse("http://localhost");

        Assert.Equal("localhost", host);
        Assert.Equal(QdrantGrpcEndpoint.DefaultGrpcPort, port);
        Assert.False(https);
    }

    /// <summary>HTTPS flag follows the URL scheme.</summary>
    [Fact]
    public void Parse_https_sets_tls_flag()
    {
        var (host, port, https) = QdrantGrpcEndpoint.Parse("https://qdrant.example:6334");

        Assert.Equal("qdrant.example", host);
        Assert.Equal(6334, port);
        Assert.True(https);
    }

    /// <summary>Custom non-REST ports are left unchanged.</summary>
    [Fact]
    public void Parse_preserves_custom_grpc_port()
    {
        var (_, port, _) = QdrantGrpcEndpoint.Parse("http://qdrant:16334");

        Assert.Equal(16334, port);
    }

    /// <summary>Invalid URLs throw.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("/relative")]
    public void Parse_rejects_invalid_url(string url)
    {
        Assert.Throws<ArgumentException>(() => QdrantGrpcEndpoint.Parse(url));
    }
}
