using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Application.Services;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CodebaseIndexer.Application.Tests;

/// <summary>
/// Memory-pressure halt path — demonstrates TUnit.Mocks setup + verify (ADR 0034 Phase 1).
/// Suite default MockBehavior is Strict via <see cref="MockDefaults"/>.
/// </summary>
public sealed class IndexEmbeddingServiceTests
{
    [Test]
    public async Task EmbedChunksAsync_memory_pressure_halt_returns_failure()
    {
        var dense = IDenseEmbedder.Mock();
        var sparse = ISparseEmbedder.Mock();
        var colbert = IColbertEmbedder.Mock();
        var memory = IMemoryPressureGuard.Mock();
        memory.Check(Any(), Any()).Returns(new MemoryPressureResult(MemoryPressureSeverity.Halt, 86));

        var service = new IndexEmbeddingService(
            dense,
            sparse,
            colbert,
            memory,
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

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error.Kind).IsEqualTo(ErrorKind.Dependency);
        await Assert.That(result.Error.Code).IsEqualTo(IndexErrorCodes.EmbedMemoryPressure);
        await Assert.That(result.Error.Message).Contains("Memory pressure");

        memory.Check(70, 85).WasCalled(Times.Once);
        dense.EmbedBatchAsync(Any(), Any()).WasCalled(Times.Never);
        sparse.EmbedBatchAsync(Any(), Any()).WasCalled(Times.Never);
        colbert.EmbedBatchAsync(Any(), Any()).WasCalled(Times.Never);
    }
}
