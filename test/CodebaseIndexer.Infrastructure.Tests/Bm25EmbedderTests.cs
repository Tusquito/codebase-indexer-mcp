using CodebaseIndexer.Infrastructure.Embedding;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Tests for BM25 sparse embedding tokenization and hashing.</summary>
public sealed class Bm25EmbedderTests
{
    /// <summary>Murmur hash token IDs match Python mmh3 output.</summary>
    [Theory]
    [InlineData("hello", 613153351)]
    [InlineData("world", 74040069)]
    [InlineData("test", 1167338989)]
    [InlineData("foo", 156908512)]
    [InlineData("return", 1430125705)]
    public void Murmur_hash_matches_python_mmh3(string token, int expectedId)
    {
        Assert.Equal(expectedId, Bm25MurmurHash3.ComputeTokenId(token));
    }

    /// <summary>Simple tokenizer output matches Python implementation.</summary>
    [Fact]
    public void Simple_tokenizer_matches_python()
    {
        var tokens = Bm25Tokenizer.Tokenize("Hello, World!  test");
        Assert.Equal(["hello", "world", "test"], tokens);
    }

    /// <summary>English snowball stemmer output matches Python implementation.</summary>
    [Theory]
    [InlineData("running", "run")]
    [InlineData("connection", "connect")]
    [InlineData("indexed", "index")]
    [InlineData("testing", "test")]
    [InlineData("return", "return")]
    [InlineData("files", "file")]
    [InlineData("stemming", "stem")]
    public void English_snowball_stemmer_matches_python(string token, string expected)
    {
        var stemmer = new EnglishSnowballStemmer();
        Assert.Equal(expected, stemmer.Stem(token));
    }
}
