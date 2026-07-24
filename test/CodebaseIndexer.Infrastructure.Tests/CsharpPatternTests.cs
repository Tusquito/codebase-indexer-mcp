using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Infrastructure.Embedding;
using CodebaseIndexer.Infrastructure.Memory;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Regression tests for core C# infrastructure patterns.</summary>
public sealed class CsharpPatternTests
{
    /// <summary>ChunkId.FromPathAndLine produces deterministic values.</summary>
    [Test]
    public async Task ChunkId_FromPathAndLine_is_deterministic()
    {
        var first = ChunkId.FromPathAndLine("src/foo.cs", 12);
        var second = ChunkId.FromPathAndLine("src/foo.cs", 12);
        await Assert.That(second).IsEqualTo(first);
        await Assert.That(ChunkId.FromPathAndLine("src/foo.cs", 13)).IsNotEqualTo(first);
    }

    /// <summary>MemoryPressureResult reports halt severity at threshold.</summary>
    [Test]
    public async Task MemoryPressureResult_reports_halt_at_threshold()
    {
        var result = new MemoryPressureResult(MemoryPressureSeverity.Halt, 91.2);
        await Assert.That(result.Severity).IsEqualTo(MemoryPressureSeverity.Halt);
        await Assert.That(result.Percent).IsEqualTo(91.2);
    }

    /// <summary>EmbedTokenLimit honors environment override source.</summary>
    [Test]
    public async Task EmbedTokenLimit_uses_env_override_source()
    {
        var limit = EmbeddingTruncation.ResolveMaxEmbedTokens(
            EmbedRole.Dense,
            "test-model",
            envTokens: 512,
            modelDir: null,
            knownRegistry: new Dictionary<string, int>(),
            logger: null);

        await Assert.That(limit.MaxTokens).IsEqualTo(512);
        await Assert.That(limit.Source).IsEqualTo(TruncationSource.EnvOverride);
    }

    /// <summary>Aspire pairing invariant: env 1024 beats registry 8192 (ADR 0035).</summary>
    [Test]
    public async Task EmbedTokenLimit_pairing_1024_overrides_registry()
    {
        var limit = EmbeddingTruncation.ResolveMaxEmbedTokens(
            EmbedRole.Dense,
            "jinaai/jina-embeddings-v2-base-code",
            envTokens: 1024,
            modelDir: null,
            knownRegistry: new Dictionary<string, int>
            {
                ["jinaai/jina-embeddings-v2-base-code"] = 8192,
            },
            logger: null);

        await Assert.That(limit.MaxTokens).IsEqualTo(1024);
        await Assert.That(limit.Source).IsEqualTo(TruncationSource.EnvOverride);
    }

    /// <summary>TruncateBm25Text preserves input shorter than the token limit.</summary>
    [Test]
    public async Task TruncatedText_preserves_short_input()
    {
        var truncated = EmbeddingTruncation.TruncateBm25Text("alpha beta gamma", maxTokens: 10);
        await Assert.That(truncated.Text).IsEqualTo("alpha beta gamma");
        await Assert.That(truncated.TokenCount).IsEqualTo(3);
    }

    /// <summary>CgroupMemoryGuard returns ok when memory is unmetered.</summary>
    [Test]
    public async Task CgroupMemoryGuard_returns_ok_when_unmetered()
    {
        var result = CgroupMemoryGuard.CheckMemoryPressure(warnPct: 70, haltPct: 85);
        await Assert.That(result.Severity).IsEqualTo(MemoryPressureSeverity.Ok);
    }
}