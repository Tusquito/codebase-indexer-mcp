using CodebaseIndexer.Infrastructure.Embedding;

namespace CodebaseIndexer.Infrastructure.Tests;

public sealed class Bm25EmbedderTests
{
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

    [Fact]
    public void Simple_tokenizer_matches_python()
    {
        var tokens = Bm25Tokenizer.Tokenize("Hello, World!  test");
        Assert.Equal(["hello", "world", "test"], tokens);
    }
}
