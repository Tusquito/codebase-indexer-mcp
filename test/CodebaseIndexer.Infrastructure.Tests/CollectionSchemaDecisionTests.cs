using CodebaseIndexer.Infrastructure.Qdrant;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>ColBERT / hybrid schema recreate matrix (Python ensure_collection mismatch parity).</summary>
public sealed class CollectionSchemaDecisionTests
{
    [Test]
    public async Task EvaluateCollectionSchema_matching_schema_keeps_collection()
    {
        var decision = QdrantVectorStore.EvaluateCollectionSchema(
            denseSize: 768,
            hasSparse: true,
            hasColbert: true,
            colbertSize: 128,
            hasQuantization: true,
            expectedDenseSize: 768,
            hybridSearch: true,
            rerankEnabled: true,
            expectedColbertTokenSize: 128,
            quantizationEnabled: true);

        await Assert.That(decision.NeedsRecreate).IsFalse();
        await Assert.That(decision.ColbertMismatch).IsFalse();
        await Assert.That(decision.ColbertDimMismatch).IsFalse();
    }

    [Test]
    public async Task EvaluateCollectionSchema_dense_dim_mismatch_recreates()
    {
        var decision = QdrantVectorStore.EvaluateCollectionSchema(
            denseSize: 384,
            hasSparse: true,
            hasColbert: false,
            colbertSize: 0,
            hasQuantization: false,
            expectedDenseSize: 768,
            hybridSearch: true,
            rerankEnabled: false,
            expectedColbertTokenSize: 128,
            quantizationEnabled: false);

        await Assert.That(decision.NeedsRecreate).IsTrue();
    }

    [Test]
    public async Task EvaluateCollectionSchema_hybrid_mismatch_recreates()
    {
        var decision = QdrantVectorStore.EvaluateCollectionSchema(
            denseSize: 768,
            hasSparse: false,
            hasColbert: false,
            colbertSize: 0,
            hasQuantization: false,
            expectedDenseSize: 768,
            hybridSearch: true,
            rerankEnabled: false,
            expectedColbertTokenSize: 128,
            quantizationEnabled: false);

        await Assert.That(decision.NeedsRecreate).IsTrue();
    }

    [Test]
    public async Task EvaluateCollectionSchema_colbert_presence_mismatch_recreates()
    {
        var decision = QdrantVectorStore.EvaluateCollectionSchema(
            denseSize: 768,
            hasSparse: true,
            hasColbert: false,
            colbertSize: 0,
            hasQuantization: true,
            expectedDenseSize: 768,
            hybridSearch: true,
            rerankEnabled: true,
            expectedColbertTokenSize: 128,
            quantizationEnabled: true);

        await Assert.That(decision.NeedsRecreate).IsTrue();
        await Assert.That(decision.ColbertMismatch).IsTrue();
    }

    [Test]
    public async Task EvaluateCollectionSchema_colbert_dim_mismatch_recreates()
    {
        var decision = QdrantVectorStore.EvaluateCollectionSchema(
            denseSize: 768,
            hasSparse: true,
            hasColbert: true,
            colbertSize: 64,
            hasQuantization: true,
            expectedDenseSize: 768,
            hybridSearch: true,
            rerankEnabled: true,
            expectedColbertTokenSize: 128,
            quantizationEnabled: true);

        await Assert.That(decision.NeedsRecreate).IsTrue();
        await Assert.That(decision.ColbertDimMismatch).IsTrue();
    }

    [Test]
    public async Task EvaluateCollectionSchema_quantization_mismatch_recreates()
    {
        var decision = QdrantVectorStore.EvaluateCollectionSchema(
            denseSize: 768,
            hasSparse: true,
            hasColbert: false,
            colbertSize: 0,
            hasQuantization: true,
            expectedDenseSize: 768,
            hybridSearch: true,
            rerankEnabled: false,
            expectedColbertTokenSize: 128,
            quantizationEnabled: false);

        await Assert.That(decision.NeedsRecreate).IsTrue();
    }
}