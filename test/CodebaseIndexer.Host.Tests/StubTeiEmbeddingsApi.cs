using System.Net;
using CodebaseIndexer.Infrastructure.Tei;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Test double for <see cref="ITeiEmbeddingsApi"/> health probes.</summary>
internal sealed class StubTeiEmbeddingsApi : ITeiEmbeddingsApi
{
    private readonly bool _healthy;

    public StubTeiEmbeddingsApi(bool healthy) => _healthy = healthy;

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var status = _healthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
        return Task.FromResult(new HttpResponseMessage(status));
    }

    /// <inheritdoc />
    public Task<EmbeddingsResponse> CreateEmbeddingsAsync(
        EmbeddingsRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
