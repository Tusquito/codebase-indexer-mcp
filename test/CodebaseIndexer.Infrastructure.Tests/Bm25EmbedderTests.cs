using CodebaseIndexer.Infrastructure.Embedding;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Tests for BM25 sparse embedding tokenization and hashing.</summary>
public sealed class Bm25EmbedderTests
{
    /// <summary>Murmur hash token IDs match Python mmh3 output.</summary>
    [Test]
    [Arguments("hello", 613153351)]
    [Arguments("world", 74040069)]
    [Arguments("test", 1167338989)]
    [Arguments("foo", 156908512)]
    [Arguments("return", 1430125705)]
    public async Task Murmur_hash_matches_python_mmh3(string token, int expectedId)
    {
        await Assert.That(Bm25MurmurHash3.ComputeTokenId(token)).IsEqualTo(expectedId);
    }

    /// <summary>Simple tokenizer output matches Python implementation.</summary>
    [Test]
    public async Task Simple_tokenizer_matches_python()
    {
        var tokens = Bm25Tokenizer.Tokenize("Hello, World!  test");
        await Assert.That(tokens).IsEquivalentTo(["hello", "world", "test"]);
    }

    /// <summary>English snowball stemmer output matches Python implementation.</summary>
    [Test]
    [Arguments("running", "run")]
    [Arguments("connection", "connect")]
    [Arguments("indexed", "index")]
    [Arguments("testing", "test")]
    [Arguments("return", "return")]
    [Arguments("files", "file")]
    [Arguments("stemming", "stem")]
    public async Task English_snowball_stemmer_matches_python(string token, string expected)
    {
        var stemmer = new EnglishSnowballStemmer();
        await Assert.That(stemmer.Stem(token)).IsEquivalentTo(expected);
    }
}