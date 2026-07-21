using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Unit tests for cross-collection RRF fusion.</summary>
public sealed class CrossCollectionRrfTests
{
    /// <summary>Fuses ranks with 1/(k+rank) and deterministic ties.</summary>
    [Fact]
    public void Fuse_prefers_higher_ranks_and_breaks_ties_deterministically()
    {
        var a1 = Hit("a", "chunk-b", 0.9);
        var a2 = Hit("a", "chunk-a", 0.8);
        var b1 = Hit("b", "chunk-a", 0.95);

        var fused = CrossCollectionRrf.Fuse(
            [
                [a1, a2],
                [b1],
            ],
            rrfK: 60,
            topK: 10);

        Assert.Equal(3, fused.Count);
        // chunk-a appears in both lists → higher fused score
        Assert.Equal("chunk-a", fused[0].Id.Value);
        Assert.Contains(fused, h => h.Id.Value == "chunk-b");
    }

    /// <summary>Respects top_k truncation.</summary>
    [Fact]
    public void Fuse_respects_top_k()
    {
        var list = Enumerable.Range(0, 5)
            .Select(i => Hit("c", $"id-{i}", 1.0 - i * 0.1))
            .ToArray();

        var fused = CrossCollectionRrf.Fuse([list], rrfK: 60, topK: 2);
        Assert.Equal(2, fused.Count);
    }

    private static SearchHit Hit(string collection, string chunkId, double score) =>
        new(
            new ChunkId(chunkId),
            score,
            "path.cs",
            "csharp",
            1,
            10,
            "Sym",
            "method",
            "content",
            collection);
}
