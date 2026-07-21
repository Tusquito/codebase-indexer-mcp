using CodebaseIndexer.Application.Search;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Pure fusion/helper smoke covering hybrid ranking parity helpers.</summary>
public sealed class HybridSearchHelperTests
{
    /// <summary>ChunkId UUID mapping stays stable (Phase 2 golden contract).</summary>
    [Fact]
    public void ChunkIdToPointUuid_is_deterministic()
    {
        var a = CodebaseIndexer.Infrastructure.Qdrant.QdrantVectorStore.ChunkIdToPointUuid("abc");
        var b = CodebaseIndexer.Infrastructure.Qdrant.QdrantVectorStore.ChunkIdToPointUuid("abc");
        Assert.Equal(a, b);
        var other = CodebaseIndexer.Infrastructure.Qdrant.QdrantVectorStore.ChunkIdToPointUuid("abd");
        Assert.False(a == other);
    }

    /// <summary>Cross-collection fuse is reachable from Infrastructure tests via Application.</summary>
    [Fact]
    public void CrossCollectionRrf_fuse_empty_lists()
    {
        var fused = CrossCollectionRrf.Fuse([], rrfK: 60, topK: 5);
        Assert.Empty(fused);
    }
}
