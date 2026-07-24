using CodebaseIndexer.Infrastructure.Qdrant;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Tests for Qdrant chunk ID to point UUID conversion.</summary>
public sealed class QdrantChunkIdTests
{
    /// <summary>ChunkIdToPointUuid matches Python uuid5 output.</summary>
    [Test]
    [Arguments("src/foo.py:10", "e76dab7b-dd95-5ceb-b893-6930a5076f1e")]
    [Arguments("a.py:1", "e81488f8-519c-5fcc-bd80-9c5a47962d6d")]
    [Arguments("b.py:2", "b179365d-40d4-5304-968e-44f54520cd2d")]
    public async Task ChunkIdToPointUuid_matches_python_uuid5(string chunkId, string expected)
    {
        var actual = QdrantVectorStore.ChunkIdToPointUuid(chunkId);
        await Assert.That(actual).IsEqualTo(expected);
    }
}