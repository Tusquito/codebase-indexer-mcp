using System.Text.Json;
using CodebaseIndexer.Proxy;

namespace CodebaseIndexer.Proxy.Tests;

/// <summary>JSON-RPC error shaping for the stdio proxy.</summary>
[NotInParallel]
public sealed class ProxyJsonRpcTests
{
    [Test]
    public async Task WriteErrorAsync_preserves_request_id()
    {
        var originalOut = Console.Out;
        try
        {
            await using var writer = new StringWriter();
            Console.SetOut(writer);
            await ProxyJsonRpc.WriteErrorAsync(
                """{"jsonrpc":"2.0","id":42,"method":"initialize"}""",
                -32000,
                "HTTP 503: unavailable");
            var line = writer.ToString().Trim();
            using var doc = JsonDocument.Parse(line);
            await Assert.That(doc.RootElement.GetProperty("id").GetInt32()).IsEqualTo(42);
            await Assert.That(doc.RootElement.GetProperty("error").GetProperty("code").GetInt32())
                .IsEqualTo(-32000);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
