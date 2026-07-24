using CodebaseIndexer.Application.Search;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Pure fusion/helper smoke covering hybrid ranking parity helpers.</summary>
public sealed class HybridSearchHelperTests
{
    /// <summary>ChunkId UUID mapping stays stable (Phase 2 golden contract).</summary>
    [Test]
    public async Task ChunkIdToPointUuid_is_deterministic()
    {
        var a = CodebaseIndexer.Infrastructure.Qdrant.QdrantVectorStore.ChunkIdToPointUuid("abc");
        var b = CodebaseIndexer.Infrastructure.Qdrant.QdrantVectorStore.ChunkIdToPointUuid("abc");
        await Assert.That(b).IsEqualTo(a);
        var other = CodebaseIndexer.Infrastructure.Qdrant.QdrantVectorStore.ChunkIdToPointUuid("abd");
        await Assert.That(a == other).IsFalse();
    }

    /// <summary>Cross-collection fuse is reachable from Infrastructure tests via Application.</summary>
    [Test]
    public async Task CrossCollectionRrf_fuse_empty_lists()
    {
        var fused = CrossCollectionRrf.Fuse([], rrfK: 60, topK: 5);
        await Assert.That(fused).IsEmpty();
    }
}