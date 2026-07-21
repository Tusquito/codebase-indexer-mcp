using CodebaseIndexer.Infrastructure.Qdrant;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>ColBERT / hybrid schema recreate matrix (Python ensure_collection mismatch parity).</summary>
public sealed class CollectionSchemaDecisionTests
{
    [Fact]
    public void EvaluateCollectionSchema_matching_schema_keeps_collection()
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

        Assert.False(decision.NeedsRecreate);
        Assert.False(decision.ColbertMismatch);
        Assert.False(decision.ColbertDimMismatch);
    }

    [Fact]
    public void EvaluateCollectionSchema_dense_dim_mismatch_recreates()
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

        Assert.True(decision.NeedsRecreate);
    }

    [Fact]
    public void EvaluateCollectionSchema_hybrid_mismatch_recreates()
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

        Assert.True(decision.NeedsRecreate);
    }

    [Fact]
    public void EvaluateCollectionSchema_colbert_presence_mismatch_recreates()
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

        Assert.True(decision.NeedsRecreate);
        Assert.True(decision.ColbertMismatch);
    }

    [Fact]
    public void EvaluateCollectionSchema_colbert_dim_mismatch_recreates()
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

        Assert.True(decision.NeedsRecreate);
        Assert.True(decision.ColbertDimMismatch);
    }

    [Fact]
    public void EvaluateCollectionSchema_quantization_mismatch_recreates()
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

        Assert.True(decision.NeedsRecreate);
    }
}
