using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Results;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.Tasks;

namespace CodebaseIndexer.Application.Tests;

/// <summary>CollectionQueryService NotFound propagation (ADR 0033 Phase 3).</summary>
public sealed class CollectionQueryServiceTests
{
    [Test]
    public async Task GetChunkAsync_missing_chunk_returns_not_found()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CollectionQueryService(new NoOpVectorStore(), cache);

        var result = await service.GetChunkAsync("missing-chunk-id", collection: "demo");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error.Kind).IsEqualTo(ErrorKind.NotFound);
        await Assert.That(result.Error.Code).IsEqualTo(StoreErrorCodes.ChunkNotFound);
        await Assert.That(result.Error.Message).Contains("missing-chunk-id");
    }
}