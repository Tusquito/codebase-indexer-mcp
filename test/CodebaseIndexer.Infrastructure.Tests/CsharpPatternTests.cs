using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Infrastructure.Embedding;
using CodebaseIndexer.Infrastructure.Memory;

namespace CodebaseIndexer.Infrastructure.Tests;

public sealed class CsharpPatternTests
{
    [Fact]
    public void ChunkId_FromPathAndLine_is_deterministic()
    {
        var first = ChunkId.FromPathAndLine("src/foo.cs", 12);
        var second = ChunkId.FromPathAndLine("src/foo.cs", 12);
        Assert.Equal(first, second);
        Assert.NotEqual(first, ChunkId.FromPathAndLine("src/foo.cs", 13));
    }

    [Fact]
    public void MemoryPressureResult_reports_halt_at_threshold()
    {
        var result = new MemoryPressureResult(MemoryPressureSeverity.Halt, 91.2);
        Assert.Equal(MemoryPressureSeverity.Halt, result.Severity);
        Assert.Equal(91.2, result.Percent);
    }

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

    [Fact]
    public void TruncatedText_preserves_short_input()
    {
        var truncated = EmbeddingTruncation.TruncateBm25Text("alpha beta gamma", maxTokens: 10);
        Assert.Equal("alpha beta gamma", truncated.Text);
        Assert.Equal(3, truncated.TokenCount);
    }

    [Fact]
    public void CgroupMemoryGuard_returns_ok_when_unmetered()
    {
        var result = CgroupMemoryGuard.CheckMemoryPressure(warnPct: 70, haltPct: 85);
        Assert.Equal(MemoryPressureSeverity.Ok, result.Severity);
    }
}
