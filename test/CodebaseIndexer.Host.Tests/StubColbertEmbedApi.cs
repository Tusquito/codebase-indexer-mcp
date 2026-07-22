using CodebaseIndexer.Infrastructure.Colbert;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Test double for <see cref="IColbertEmbedApi"/> health probes.</summary>
internal sealed class StubColbertEmbedApi : IColbertEmbedApi
{
    private readonly bool _healthy;

    public StubColbertEmbedApi(bool healthy) => _healthy = healthy;

    /// <inheritdoc />
    public Task<ColbertHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!_healthy)
        {
            throw new HttpRequestException("ColBERT sidecar unreachable");
        }

        return Task.FromResult(new ColbertHealthResponse(
            Model: "colbert-ir/colbertv2.0",
            TokenDimension: 128,
            Loaded: true,
            Device: "cpu",
            ExecutionProviders: ["CPUExecutionProvider"],
            CudaAvailable: false));
    }

    /// <inheritdoc />
    public Task<ColbertEmbedResponse> EmbedColbertAsync(
        ColbertEmbedRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
