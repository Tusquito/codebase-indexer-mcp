using System.Net.Http.Headers;
using System.Text;
using CodebaseIndexer.Proxy;

// Stdio → HTTP MCP forwarder (parity with mcp_server/.../stdio_proxy.py).
// LogToStandardErrorThreshold = Trace — all diagnostics on stderr; stdout is JSON-RPC only.
Console.Error.WriteLine("[Trace] stdio_proxy_start");

var mcpUrl = Environment.GetEnvironmentVariable("MCP_URL") ?? "http://localhost:8000/mcp";
var authToken = Environment.GetEnvironmentVariable("MCP_AUTH_TOKEN") ?? string.Empty;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
string? sessionId = null;

while (true)
{
    var line = await Console.In.ReadLineAsync().ConfigureAwait(false);
    if (line is null)
    {
        break;
    }

    line = line.Trim();
    if (line.Length == 0)
    {
        continue;
    }

    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, mcpUrl)
        {
            Content = new StringContent(line, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrEmpty(authToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }

        if (!string.IsNullOrEmpty(sessionId))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);

        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sidValues))
        {
            sessionId = sidValues.FirstOrDefault() ?? sessionId;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } sseLine)
            {
                if (!sseLine.StartsWith("data: ", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = sseLine["data: ".Length..];
                if (data.Length == 0)
                {
                    continue;
                }

                await Console.Out.WriteLineAsync(data).ConfigureAwait(false);
                await Console.Out.FlushAsync().ConfigureAwait(false);
            }
        }
        else
        {
            var body = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
            if (body.Length > 0)
            {
                await Console.Out.WriteLineAsync(body).ConfigureAwait(false);
                await Console.Out.FlushAsync().ConfigureAwait(false);
            }
        }
    }
    catch (HttpRequestException ex)
    {
        await ProxyJsonRpc.WriteErrorAsync(line, -32000, $"HTTP error: {ex.Message}").ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        await ProxyJsonRpc.WriteErrorAsync(line, -32000, ex.Message).ConfigureAwait(false);
    }
}
