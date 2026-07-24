using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Application.Tests;

public sealed class IndexEmbeddingServiceTests
{
    [Fact]
    public async Task EmbedChunksAsync_memory_pressure_halt_returns_failure()
    {
        var service = new IndexEmbeddingService(
            new StubDense(),
            new StubSparse(),
            new StubColbert(),
            new HaltMemoryGuard(),
            MsOptions.Create(new EmbeddingOptions
            {
                HybridSearch = false,
                RerankEnabled = false,
            }),
            MsOptions.Create(new IndexingOptions
            {
                MemoryPressureWarnPct = 70,
                MemoryPressureHaltPct = 85,
                SequentialEmbed = false,
            }),
            NullLogger<IndexEmbeddingService>.Instance);

        var chunk = new Chunk(
            new ChunkId("c1"),
            "a.py",
            "def hello(): pass",
            1,
            1,
            "hello",
            SourceLanguage.Python,
            "sha",
            SymbolType.Function);

        var result = await service.EmbedChunksAsync([chunk]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Dependency, result.Error.Kind);
        Assert.Equal(IndexErrorCodes.EmbedMemoryPressure, result.Error.Code);
        Assert.Contains("Memory pressure", result.Error.Message, StringComparison.Ordinal);
    }

    private sealed class HaltMemoryGuard : IMemoryPressureGuard
    {
        public MemoryPressureResult Check(int warnPct, int haltPct) =>
            new(MemoryPressureSeverity.Halt, haltPct + 1);
    }

    private sealed class StubDense : IDenseEmbedder
    {
        public int VectorSize => 2;
        public bool IsLoaded => true;
        public Task<Result> PreloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public void Release()
        {
        }

        public Task<Result<IReadOnlyList<IReadOnlyList<float>>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<IReadOnlyList<float>>>.Success(
                texts.Select(_ => (IReadOnlyList<float>)new float[] { 0.1f, 0.2f }).ToArray()));

        public Task<Result<IReadOnlyList<IReadOnlyList<float>>>> EmbedQueryAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            EmbedBatchAsync(texts, cancellationToken);
    }

    private sealed class StubSparse : ISparseEmbedder
    {
        public bool IsLoaded => true;
        public Task<Result> PreloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public void Release()
        {
        }

        public Task<Result<IReadOnlyList<SparseVector>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<SparseVector>>.Success(
                texts.Select(_ => new SparseVector([], [])).ToArray()));
    }

    private sealed class StubColbert : IColbertEmbedder
    {
        public int TokenDimension => 128;
        public bool IsLoaded => true;
        public Task<Result> PreloadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public void Release()
        {
        }

        public Task<Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>> EmbedBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>>>.Success(
                texts.Select(_ => (IReadOnlyList<IReadOnlyList<float>>)Array.Empty<IReadOnlyList<float>>()).ToArray()));
    }
}
