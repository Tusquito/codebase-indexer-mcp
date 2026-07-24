using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using CodebaseIndexer.Host.Tools;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Host.Tests;

/// <summary>IndexTools maps Validation/NotFound through the unified MCP envelope.</summary>
public sealed class IndexToolsEnvelopeTests
{
    [Test]
    public async Task IndexCodebase_root_path_returns_validation_envelope()
    {
        var tools = CreateTools(new FakeJobs(), new FakeStore());
        var payload = await tools.IndexCodebaseAsync(path: "/");

        var envelope = await Assert.That(payload).IsTypeOf<McpErrorEnvelope>();
        await Assert.That(envelope!.Error.Kind).IsEqualTo(ErrorKind.Validation);
        await Assert.That(envelope.Error.Code).IsEqualTo(McpErrorCodes.PathRequired);
        await Assert.That(envelope.Error.Message.Contains("project folder", StringComparison.OrdinalIgnoreCase))
            .IsTrue();
        await Assert.That(envelope.Error.Metadata).IsNotNull();
        await Assert.That(envelope.Error.Metadata!.ContainsKey("hint")).IsTrue();
    }

    [Test]
    public async Task IndexStatus_missing_job_returns_not_found_envelope()
    {
        var tools = CreateTools(
            new FakeJobs
            {
                JobResult = Result<IndexJobSnapshot>.Failure(new Error(
                    ErrorKind.NotFound,
                    IndexErrorCodes.JobNotFound,
                    "No indexing job found for 'missing'.")),
            },
            new FakeStore());

        var payload = await tools.IndexStatusAsync(collection: "missing");

        var envelope = await Assert.That(payload).IsTypeOf<McpErrorEnvelope>();
        await Assert.That(envelope!.Error.Kind).IsEqualTo(ErrorKind.NotFound);
        await Assert.That(envelope.Error.Code).IsEqualTo(IndexErrorCodes.JobNotFound);
    }

    [Test]
    public async Task IndexAll_empty_collections_returns_not_found_envelope()
    {
        var tools = CreateTools(new FakeJobs(), new FakeStore { Collections = [] });
        var payload = await tools.IndexAllAsync();

        var envelope = await Assert.That(payload).IsTypeOf<McpErrorEnvelope>();
        await Assert.That(envelope!.Error.Kind).IsEqualTo(ErrorKind.NotFound);
        await Assert.That(envelope.Error.Code).IsEqualTo(McpErrorCodes.CollectionsEmpty);
    }

    private static IndexTools CreateTools(IIndexJobService jobs, IVectorStore store) =>
        new(
            jobs,
            store,
            MsOptions.Create(new WorkspaceOptions
            {
                Path = "/workspace",
                ExcludedDirs = string.Empty,
                HashWorkerDop = 1,
                ReadaheadBuffer = 1,
            }));

    private sealed class FakeJobs : IIndexJobService
    {
        public Result<IndexJobSnapshot> JobResult { get; init; } = Result<IndexJobSnapshot>.Failure(
            new Error(ErrorKind.NotFound, IndexErrorCodes.JobNotFound, "missing"));

        public ValueTask<bool> IsRunningAsync(string collection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<Result<IndexJobSnapshot>> GetJobAsync(
            string collection,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(JobResult);

        public ValueTask<IReadOnlyList<IndexJobSnapshot>> GetAllJobsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<IndexJobSnapshot>>([]);

        public Task<Result<IndexJobSnapshot>> StartAsync(
            IndexCodebaseCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IndexJobSnapshot>.Failure(
                new Error(ErrorKind.Internal, "test.unexpected_start", "Start should not run in this test.")));

        public ValueTask<Result<IndexJobSnapshot>> CancelAsync(
            string collection,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<IndexJobSnapshot>.Failure(
                new Error(ErrorKind.NotFound, IndexErrorCodes.JobNotFound, "missing")));

        public Task<IReadOnlyList<IndexJobSnapshot>> IndexAllAsync(
            IndexAllCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IndexJobSnapshot>>([]);
    }

    // Human decision: keep FakeStore — Strict IVectorStore mock setup cost is high.
    private sealed class FakeStore : IVectorStore
    {
        public IReadOnlyList<string> Collections { get; init; } = [];

        public ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public Task<Result> EnsureCollectionAsync(string collection, bool force = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> UpsertChunksAsync(
            string collection,
            IReadOnlyList<EmbeddedChunk> chunks,
            bool omitCallees = false,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? graphNodeIdsByChunk = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> SetCollectionGraphCallSitesAsync(
            string collection,
            bool enabled = true,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> SetCollectionGraphEnabledAsync(
            string collection,
            bool enabled = true,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public ValueTask<bool> CollectionHasGraphCallSitesAsync(
            string collection,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public ValueTask<bool> CollectionHasGraphEnabledAsync(
            string collection,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public Task<Result<IReadOnlyList<SearchHit>>> SearchAsync(
            string collection,
            IReadOnlyList<float> denseVector,
            SparseVector? sparseVector,
            int topK,
            SourceLanguage? language = null,
            float minScore = 0.5f,
            IReadOnlyList<IReadOnlyList<float>>? colbertVector = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success([]));

        public Task<Result<ChunkPayload>> GetChunkByIdAsync(
            string collection,
            string chunkId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ChunkPayload>.Failure(new Error(
                ErrorKind.NotFound, StoreErrorCodes.ChunkNotFound, "missing")));

        public Task<Result<ChunkPayload>> FindChunkByIdAsync(
            string chunkId,
            string? collection = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ChunkPayload>.Failure(new Error(
                ErrorKind.NotFound, StoreErrorCodes.ChunkNotFound, "missing")));

        public Task<Result<IReadOnlyList<FileSymbol>>> ScrollFileSymbolsAsync(
            string collection,
            string relPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<FileSymbol>>.Success([]));

        public Task<Result<IReadOnlyList<PayloadRow>>> ScrollAllPayloadsAsync(
            string collection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<PayloadRow>>.Success([]));

        public Task<Result<IReadOnlyList<CollectionStats>>> ListCollectionStatsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<CollectionStats>>.Success([]));

        public Task<Result<IReadOnlyList<SearchHit>>> FindSymbolInCollectionsAsync(
            string symbolName,
            IReadOnlyList<string> collections,
            int limitPerCollection = 10,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success([]));

        public Task<Result<IReadOnlyList<string>>> ListCollectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<string>>.Success(Collections));

        public ValueTask<Result<CollectionStats>> GetCollectionStatsAsync(
            string collection,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<CollectionStats>.Failure(new Error(
                ErrorKind.NotFound, StoreErrorCodes.CollectionNotFound, "missing")));

        public Task<Result<IReadOnlyDictionary<string, FileMetadata>>> GetFileMetadataAsync(
            string collection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyDictionary<string, FileMetadata>>.Success(
                new Dictionary<string, FileMetadata>()));

        public Task<Result> DeleteByPathsAsync(
            string collection,
            IReadOnlyList<string> relPaths,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> SetIndexingAsync(
            string collection,
            bool enabled,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> VerifyChunkIdsExistAsync(
            string collection,
            IReadOnlyList<string> chunkIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<IReadOnlyList<SearchHit>>> RecommendAsync(
            string collection,
            IReadOnlyList<RecommendExample> positive,
            IReadOnlyList<RecommendExample>? negative = null,
            int limit = 5,
            SourceLanguage? language = null,
            string? pathGlob = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success([]));

        public Task<Result<IReadOnlyList<SearchHit>>> FindOutlierChunksAsync(
            string collection,
            IReadOnlyList<string>? contextChunkIds = null,
            int limit = 5,
            SourceLanguage? language = null,
            string? pathGlob = null,
            float? maxSimilarity = null,
            int? maxContextSamples = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success([]));

        public Task<Result<IReadOnlyList<SearchHit>>> FindCallersInCollectionsAsync(
            string method,
            IReadOnlyList<string> collections,
            string? receiver = null,
            int limitPerCollection = 10,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<SearchHit>>.Success([]));

        public Task<Result<IReadOnlyList<IReadOnlyDictionary<string, string>>>> ScrollChunksByPathsAsync(
            string collection,
            IReadOnlyList<string> relPaths,
            IReadOnlyList<string>? payloadFields = null,
            int limit = 500,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<IReadOnlyDictionary<string, string>>>.Success([]));
    }
}
