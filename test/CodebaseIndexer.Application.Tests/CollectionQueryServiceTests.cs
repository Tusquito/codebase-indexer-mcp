using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Results;
using Microsoft.Extensions.Caching.Memory;

namespace CodebaseIndexer.Application.Tests;

/// <summary>CollectionQueryService NotFound propagation (ADR 0033 Phase 3).</summary>
public sealed class CollectionQueryServiceTests
{
    [Fact]
    public async Task GetChunkAsync_missing_chunk_returns_not_found()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CollectionQueryService(new NoOpVectorStore(), cache);

        var result = await service.GetChunkAsync("missing-chunk-id", collection: "demo");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Equal(StoreErrorCodes.ChunkNotFound, result.Error.Code);
        Assert.Contains("missing-chunk-id", result.Error.Message, StringComparison.Ordinal);
    }
}
