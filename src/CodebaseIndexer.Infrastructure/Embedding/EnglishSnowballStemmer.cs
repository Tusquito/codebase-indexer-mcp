using Lucene.Net.Tartarus.Snowball.Ext;

namespace CodebaseIndexer.Infrastructure.Embedding;

/// <summary>
/// English Snowball stemmer via Apache Lucene.NET (matches fastembed/py-rust-stemmers algorithm family).
/// </summary>
internal sealed class EnglishSnowballStemmer
{
    private readonly EnglishStemmer _stemmer = new();

    public string Stem(string token)
    {
        _stemmer.SetCurrent(token);
        return _stemmer.Stem() ? _stemmer.Current : token;
    }
}
