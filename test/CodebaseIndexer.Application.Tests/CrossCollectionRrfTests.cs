using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Domain.Models;
using System.Threading.Tasks;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Unit tests for cross-collection RRF fusion.</summary>
public sealed class CrossCollectionRrfTests
{
    /// <summary>Fuses ranks with 1/(k+rank) and deterministic ties.</summary>
    [Test]
    public async Task Fuse_prefers_higher_ranks_and_breaks_ties_deterministically()
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

        await Assert.That(fused.Count).IsEqualTo(3);
        // chunk-a appears in both lists → higher fused score
        await Assert.That(fused[0].Id.Value).IsEqualTo("chunk-a");
        await Assert.That(fused).Contains(h => h.Id.Value == "chunk-b");
    }

    /// <summary>Respects top_k truncation.</summary>
    [Test]
    public async Task Fuse_respects_top_k()
    {
        var list = Enumerable.Range(0, 5)
            .Select(i => Hit("c", $"id-{i}", 1.0 - i * 0.1))
            .ToArray();

        var fused = CrossCollectionRrf.Fuse([list], rrfK: 60, topK: 2);
        await Assert.That(fused.Count).IsEqualTo(2);
    }

    private static SearchHit Hit(string collection, string chunkId, double score) =>
        new(
            new ChunkId(chunkId),
            score,
            "path.cs",
            SourceLanguage.CSharp,
            1,
            10,
            "Sym",
            SymbolType.Method,
            "content",
            collection);
}