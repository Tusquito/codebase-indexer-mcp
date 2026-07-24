using CodebaseIndexer.Infrastructure.Qdrant;
using Qdrant.Client.Grpc;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Adaptive ColBERT skip / prefetch helpers (Python <c>test_qdrant_search</c> parity).</summary>
public sealed class AdaptiveRerankDecisionTests
{
    [Test]
    public async Task ShouldSkipColbertAfterProbe_large_gap_skips()
    {
        var probe = new List<ScoredPoint>
        {
            new() { Score = 0.10f },
            new() { Score = 0.05f },
        };

        await Assert.That(QdrantVectorStore.ShouldSkipColbertAfterProbe(probe, gapThreshold: 0.02f)).IsTrue();
    }

    [Test]
    public async Task ShouldSkipColbertAfterProbe_small_gap_runs()
    {
        var probe = new List<ScoredPoint>
        {
            new() { Score = 0.10f },
            new() { Score = 0.09f },
        };

        await Assert.That(QdrantVectorStore.ShouldSkipColbertAfterProbe(probe, gapThreshold: 0.02f)).IsFalse();
    }

    [Test]
    public async Task ShouldSkipColbertAfterProbe_fewer_than_two_hits_runs()
    {
        var probe = new List<ScoredPoint> { new() { Score = 0.10f } };
        await Assert.That(QdrantVectorStore.ShouldSkipColbertAfterProbe(probe, gapThreshold: 0.02f)).IsFalse();
    }

    [Test]
    public async Task ResolveSearchPrefetchLimit_rerank_uses_rerank_prefetch()
    {
        var limit = QdrantVectorStore.ResolveSearchPrefetchLimit(
            usedRerank: true,
            rerankPrefetch: 77,
            topK: 5,
            prefetchMultiplier: 5);
        await Assert.That(limit).IsEqualTo(77u);
    }

    [Test]
    public async Task ResolveSearchPrefetchLimit_hybrid_uses_top_k_multiplier()
    {
        var limit = QdrantVectorStore.ResolveSearchPrefetchLimit(
            usedRerank: false,
            rerankPrefetch: 77,
            topK: 5,
            prefetchMultiplier: 5);
        await Assert.That(limit).IsEqualTo(25u);
    }

    [Test]
    public async Task AdaptiveRerankStats_reset_clears_counters()
    {
        var stats = new AdaptiveRerankStats
        {
            Total = 3,
            Skipped = 2,
            Reranked = 1,
        };
        stats.Reset();
        await Assert.That(stats.Total).IsEqualTo(0);
        await Assert.That(stats.Skipped).IsEqualTo(0);
        await Assert.That(stats.Reranked).IsEqualTo(0);
    }
}