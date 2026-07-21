using CodebaseIndexer.Infrastructure.Qdrant;
using Qdrant.Client.Grpc;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Adaptive ColBERT skip / prefetch helpers (Python <c>test_qdrant_search</c> parity).</summary>
public sealed class AdaptiveRerankDecisionTests
{
    [Fact]
    public void ShouldSkipColbertAfterProbe_large_gap_skips()
    {
        var probe = new List<ScoredPoint>
        {
            new() { Score = 0.10f },
            new() { Score = 0.05f },
        };

        Assert.True(QdrantVectorStore.ShouldSkipColbertAfterProbe(probe, gapThreshold: 0.02f));
    }

    [Fact]
    public void ShouldSkipColbertAfterProbe_small_gap_runs()
    {
        var probe = new List<ScoredPoint>
        {
            new() { Score = 0.10f },
            new() { Score = 0.09f },
        };

        Assert.False(QdrantVectorStore.ShouldSkipColbertAfterProbe(probe, gapThreshold: 0.02f));
    }

    [Fact]
    public void ShouldSkipColbertAfterProbe_fewer_than_two_hits_runs()
    {
        var probe = new List<ScoredPoint> { new() { Score = 0.10f } };
        Assert.False(QdrantVectorStore.ShouldSkipColbertAfterProbe(probe, gapThreshold: 0.02f));
    }

    [Fact]
    public void ResolveSearchPrefetchLimit_rerank_uses_rerank_prefetch()
    {
        var limit = QdrantVectorStore.ResolveSearchPrefetchLimit(
            usedRerank: true,
            rerankPrefetch: 77,
            topK: 5,
            prefetchMultiplier: 5);
        Assert.Equal(77u, limit);
    }

    [Fact]
    public void ResolveSearchPrefetchLimit_hybrid_uses_top_k_multiplier()
    {
        var limit = QdrantVectorStore.ResolveSearchPrefetchLimit(
            usedRerank: false,
            rerankPrefetch: 77,
            topK: 5,
            prefetchMultiplier: 5);
        Assert.Equal(25u, limit);
    }

    [Fact]
    public void AdaptiveRerankStats_reset_clears_counters()
    {
        var stats = new AdaptiveRerankStats
        {
            Total = 3,
            Skipped = 2,
            Reranked = 1,
        };
        stats.Reset();
        Assert.Equal(0, stats.Total);
        Assert.Equal(0, stats.Skipped);
        Assert.Equal(0, stats.Reranked);
    }
}
