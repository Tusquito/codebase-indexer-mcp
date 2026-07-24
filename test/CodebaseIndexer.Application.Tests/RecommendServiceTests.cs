using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using MsOptions = Microsoft.Extensions.Options.Options;
using CodebaseIndexer.Domain.Results;
using System.Threading.Tasks;

namespace CodebaseIndexer.Application.Tests;

/// <summary>Port of test_recommend_tool.py / outlier validation.</summary>
public sealed class RecommendServiceTests
{
    [Test]
    public async Task RecommendCode_requires_positive_example()
    {
        var service = CreateService(new FakeStore());
        var result = await service.RecommendCodeAsync("proj");
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error.Kind).IsEqualTo(ErrorKind.Validation);
        await Assert.That(result.Error.Message).Contains("positive");
    }

    [Test]
    public async Task RecommendCode_caps_limit_at_20()
    {
        var store = new FakeStore();
        var service = CreateService(store);
        await service.RecommendCodeAsync("proj", positiveChunkIds: ["a"], limit: 50);
        await Assert.That(store.LastRecommendLimit).IsEqualTo(20);
    }

    [Test]
    public async Task RecommendCode_enforces_max_examples()
    {
        var service = CreateService(new FakeStore(), recommendMaxExamples: 2);
        var result = await service.RecommendCodeAsync(
            "proj",
            positiveChunkIds: ["a", "b"],
            negativeChunkIds: ["c"]);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error.Message).Contains("RECOMMEND_MAX_EXAMPLES");
    }

    [Test]
    public async Task RecommendCode_verifies_ids_embeds_and_shapes_results()
    {
        var store = new FakeStore
        {
            RecommendHits =
            [
                new SearchHit(
                    new ChunkId("src/h.py:1"), 0.9, "src/h.py", SourceLanguage.Python, 1, 10,
                    "handler", SymbolType.Function, "def handler(): ...", "proj"),
            ],
        };
        var dense = new FakeDense();
        var service = new RecommendService(
            store,
            dense,
            MsOptions.Create(new DiscoveryOptions
            {
                RecommendEnabled = true,
                RecommendMaxExamples = 10,
                OutlierMaxContextSamples = 200,
                OutlierMaxSimilarity = 0.55f,
            }));

        var result = await service.RecommendCodeAsync(
            "proj",
            positiveChunkIds: ["pos"],
            positiveQuery: "handler pattern",
            negativeChunkIds: ["neg"],
            negativeQuery: "test utilities",
            pathGlob: "src/*.py");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(store.VerifiedIds).IsEquivalentTo(["pos", "neg"]);
        await Assert.That(dense.LastTexts).IsEquivalentTo(["handler pattern", "test utilities"]);
        await Assert.That(store.LastPositiveCount).IsEqualTo(2);
        await Assert.That(store.LastNegativeCount).IsEqualTo(2);
        var response = await Assert.That(result.Value).IsTypeOf<RecommendCodeResponse>();
        await Assert.That(response!.Results).HasSingleItem();
        await Assert.That(response.PositiveExamples).IsEqualTo(2);
        await Assert.That(response.NegativeExamples).IsEqualTo(2);
    }

    [Test]
    public async Task FindOutlierChunks_rejects_invalid_max_similarity()
    {
        var service = CreateService(new FakeStore());
        var result = await service.FindOutlierChunksAsync("proj", maxSimilarity: 1.5f);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error.Kind).IsEqualTo(ErrorKind.Validation);
    }

    private static RecommendService CreateService(FakeStore store, int recommendMaxExamples = 10) =>
        new(
            store,
            new FakeDense(),
            MsOptions.Create(new DiscoveryOptions
            {
                RecommendEnabled = true,
                RecommendMaxExamples = recommendMaxExamples,
                OutlierMaxContextSamples = 200,
                OutlierMaxSimilarity = 0.55f,
            }));

    private sealed class FakeDense : IDenseEmbedder
    {
        public IReadOnlyList<string>? LastTexts { get; private set; }
        public int VectorSize => 2;
        public bool IsLoaded => true;
        public Task<Result> PreloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public void Release() { }
        public Task<Result<IReadOnlyList<IReadOnlyList<float>>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            LastTexts = texts.ToArray();
            return Task.FromResult(Result<IReadOnlyList<IReadOnlyList<float>>>.Success(
                texts.Select(_ => (IReadOnlyList<float>)new float[] { 0.2f, 0.3f }).ToArray()));
        }
        public Task<Result<IReadOnlyList<IReadOnlyList<float>>>> EmbedQueryAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            EmbedBatchAsync(texts, cancellationToken);
    }

    private sealed class FakeStore : NoOpVectorStore
    {
        public IReadOnlyList<string>? VerifiedIds { get; private set; }
        public int LastRecommendLimit { get; private set; }
        public int LastPositiveCount { get; private set; }
        public int LastNegativeCount { get; private set; }
        public IReadOnlyList<SearchHit> RecommendHits { get; init; } = [];

        public override Task<Result> VerifyChunkIdsExistAsync(string collection, IReadOnlyList<string> chunkIds, CancellationToken cancellationToken = default)
        {
            VerifiedIds = chunkIds.ToArray();
            return Task.FromResult(Result.Success());
        }

        public override Task<Result<IReadOnlyList<SearchHit>>> RecommendAsync(
            string collection,
            IReadOnlyList<RecommendExample> positive,
            IReadOnlyList<RecommendExample>? negative = null,
            int limit = 5,
            SourceLanguage? language = null,
            string? pathGlob = null,
            CancellationToken cancellationToken = default)
        {
            LastRecommendLimit = limit;
            LastPositiveCount = positive.Count;
            LastNegativeCount = negative?.Count ?? 0;
            return Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success(RecommendHits));
        }
    }
}