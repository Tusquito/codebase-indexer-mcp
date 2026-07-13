using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Infrastructure.Embedding;
using CodebaseIndexer.Infrastructure.Memory;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Regression tests for core C# infrastructure patterns.</summary>
public sealed class CsharpPatternTests
{
    /// <summary>ChunkId.FromPathAndLine produces deterministic values.</summary>
    [Fact]
    public void ChunkId_FromPathAndLine_is_deterministic()
    {
        var first = ChunkId.FromPathAndLine("src/foo.cs", 12);
        var second = ChunkId.FromPathAndLine("src/foo.cs", 12);
        Assert.Equal(first, second);
        Assert.NotEqual(first, ChunkId.FromPathAndLine("src/foo.cs", 13));
    }

    /// <summary>MemoryPressureResult reports halt severity at threshold.</summary>
    [Fact]
    public void MemoryPressureResult_reports_halt_at_threshold()
    {
        var result = new MemoryPressureResult(MemoryPressureSeverity.Halt, 91.2);
        Assert.Equal(MemoryPressureSeverity.Halt, result.Severity);
        Assert.Equal(91.2, result.Percent);
    }

    /// <summary>EmbedTokenLimit honors environment override source.</summary>
    [Fact]
    public void EmbedTokenLimit_uses_env_override_source()
    {
        var limit = EmbeddingTruncation.ResolveMaxEmbedTokens(
            EmbedRole.Dense,
            "test-model",
            envTokens: 512,
            modelDir: null,
            knownRegistry: new Dictionary<string, int>(),
            logger: null);

        Assert.Equal(512, limit.MaxTokens);
        Assert.Equal(TruncationSource.EnvOverride, limit.Source);
    }

    /// <summary>TruncateBm25Text preserves input shorter than the token limit.</summary>
    [Fact]
    public void TruncatedText_preserves_short_input()
    {
        var truncated = EmbeddingTruncation.TruncateBm25Text("alpha beta gamma", maxTokens: 10);
        Assert.Equal("alpha beta gamma", truncated.Text);
        Assert.Equal(3, truncated.TokenCount);
    }

    /// <summary>CgroupMemoryGuard returns ok when memory is unmetered.</summary>
    [Fact]
    public void CgroupMemoryGuard_returns_ok_when_unmetered()
    {
        var result = CgroupMemoryGuard.CheckMemoryPressure(warnPct: 70, haltPct: 85);
        Assert.Equal(MemoryPressureSeverity.Ok, result.Severity);
    }
}
