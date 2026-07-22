using CodebaseIndexer.AppHost;

namespace CodebaseIndexer.AppHost.Tests.Tests;

/// <summary>Unit tests for Aspire TEI / client dense-token pairing (ADR 0035).</summary>
public sealed class TeiBatchTokenPairingTests
{
    /// <summary>Missing or blank TEI env resolves to 1024.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveTeiMaxBatchTokens_missing_or_blank_defaults_to_1024(string? value)
    {
        Assert.Equal("1024", TeiBatchTokenPairing.ResolveTeiMaxBatchTokens(value));
    }

    /// <summary>Explicit TEI env override is honored.</summary>
    [Fact]
    public void ResolveTeiMaxBatchTokens_honors_explicit_override()
    {
        Assert.Equal("8192", TeiBatchTokenPairing.ResolveTeiMaxBatchTokens("8192"));
        Assert.Equal("2048", TeiBatchTokenPairing.ResolveTeiMaxBatchTokens(" 2048 "));
    }

    /// <summary>Missing or blank client env resolves to 1024.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", null)]
    [InlineData(null, "   ")]
    public void ResolveClientMaxDenseTokens_missing_or_blank_defaults_to_1024(
        string? embeddingMaxDenseTokens,
        string? maxDenseEmbedTokens)
    {
        Assert.Equal(
            "1024",
            TeiBatchTokenPairing.ResolveClientMaxDenseTokens(
                embeddingMaxDenseTokens,
                maxDenseEmbedTokens));
    }

    /// <summary>Nested Embedding__MaxDenseTokens wins over flat MAX_DENSE_EMBED_TOKENS.</summary>
    [Fact]
    public void ResolveClientMaxDenseTokens_prefers_nested_over_flat()
    {
        Assert.Equal(
            "2048",
            TeiBatchTokenPairing.ResolveClientMaxDenseTokens("2048", "8192"));
    }

    /// <summary>Flat MAX_DENSE_EMBED_TOKENS is used when nested is blank.</summary>
    [Fact]
    public void ResolveClientMaxDenseTokens_uses_flat_when_nested_blank()
    {
        Assert.Equal(
            "4096",
            TeiBatchTokenPairing.ResolveClientMaxDenseTokens(null, "4096"));
        Assert.Equal(
            "512",
            TeiBatchTokenPairing.ResolveClientMaxDenseTokens("  ", "512"));
    }
}
