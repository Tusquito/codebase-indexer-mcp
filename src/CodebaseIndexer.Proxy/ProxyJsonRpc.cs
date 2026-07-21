using System.Text.Json;

namespace CodebaseIndexer.Proxy;

/// <summary>JSON-RPC error helpers for the stdio proxy.</summary>
internal static class ProxyJsonRpc
{
    /// <summary>Writes a JSON-RPC error object to stdout.</summary>
    public static async Task WriteErrorAsync(string requestLine, int code, string message)
    {
        object? id = null;
        try
        {
            using var doc = JsonDocument.Parse(requestLine);
            if (doc.RootElement.TryGetProperty("id", out var idEl))
            {
                id = idEl.ValueKind switch
                {
                    JsonValueKind.Number => idEl.TryGetInt64(out var n) ? n : idEl.GetDouble(),
                    JsonValueKind.String => idEl.GetString(),
                    _ => null,
                };
            }
        }
        catch
        {
            // ignore parse errors for id extraction
        }

        var err = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            error = new { code, message = message.Length > 200 ? message[..200] : message },
        });
        await Console.Out.WriteLineAsync(err).ConfigureAwait(false);
        await Console.Out.FlushAsync().ConfigureAwait(false);
    }
}
