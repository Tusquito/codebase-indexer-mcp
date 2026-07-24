using CodebaseIndexer.Infrastructure.Qdrant;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Tests for Qdrant gRPC URL/port parsing used by <c>Qdrant.Client</c>.</summary>
public sealed class QdrantGrpcEndpointTests
{
    /// <summary>Explicit gRPC port is preserved.</summary>
    [Test]
    public async Task Parse_preserves_grpc_port_6334()
    {
        var (host, port, https) = QdrantGrpcEndpoint.Parse("http://qdrant:6334");

        await Assert.That(host).IsEqualTo("qdrant");
        await Assert.That(port).IsEqualTo(QdrantGrpcEndpoint.DefaultGrpcPort);
        await Assert.That(https).IsFalse();
    }

    /// <summary>Well-known REST port is remapped to gRPC (avoids PROTOCOL_ERROR).</summary>
    [Test]
    public async Task Parse_remaps_rest_port_6333_to_grpc_6334()
    {
        var (host, port, https) = QdrantGrpcEndpoint.Parse("http://qdrant:6333");

        await Assert.That(host).IsEqualTo("qdrant");
        await Assert.That(port).IsEqualTo(QdrantGrpcEndpoint.DefaultGrpcPort);
        await Assert.That(https).IsFalse();
    }

    /// <summary>Scheme without an explicit port uses the Qdrant gRPC default.</summary>
    [Test]
    public async Task Parse_default_http_port_uses_grpc_default()
    {
        var (host, port, https) = QdrantGrpcEndpoint.Parse("http://localhost");

        await Assert.That(host).IsEqualTo("localhost");
        await Assert.That(port).IsEqualTo(QdrantGrpcEndpoint.DefaultGrpcPort);
        await Assert.That(https).IsFalse();
    }

    /// <summary>HTTPS flag follows the URL scheme.</summary>
    [Test]
    public async Task Parse_https_sets_tls_flag()
    {
        var (host, port, https) = QdrantGrpcEndpoint.Parse("https://qdrant.example:6334");

        await Assert.That(host).IsEqualTo("qdrant.example");
        await Assert.That(port).IsEqualTo(6334);
        await Assert.That(https).IsTrue();
    }

    /// <summary>Custom non-REST ports are left unchanged.</summary>
    [Test]
    public async Task Parse_preserves_custom_grpc_port()
    {
        var (_, port, _) = QdrantGrpcEndpoint.Parse("http://qdrant:16334");

        await Assert.That(port).IsEqualTo(16334);
    }

    /// <summary>Invalid URLs throw.</summary>
    [Test]
    [Arguments("")]
    [Arguments("not-a-url")]
    [Arguments("/relative")]
    public void Parse_rejects_invalid_url(string url)
    {
        Assert.Throws<ArgumentException>(() => QdrantGrpcEndpoint.Parse(url));
    }
}