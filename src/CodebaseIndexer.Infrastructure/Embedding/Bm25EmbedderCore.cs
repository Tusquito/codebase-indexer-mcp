using System.Collections.Frozen;
using System.Globalization;
using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Infrastructure.Embedding;

internal sealed class Bm25EmbedderCore
{
    private const float K = 1.2f;
    private const float B = 0.75f;
    private const float AvgLen = 256f;
    private const int TokenMaxLength = 40;

    private static readonly FrozenSet<string> Punctuation = BuildPunctuationSet();

    private readonly HashSet<string> _stopwords;
    private readonly EnglishSnowballStemmer _stemmer;

    public Bm25EmbedderCore(string modelDir, bool disableStemmer = false)
    {
        if (disableStemmer)
        {
            _stopwords = new HashSet<string>(StringComparer.Ordinal);
            _stemmer = new EnglishSnowballStemmer();
        }
        else
        {
            _stopwords = LoadStopwords(modelDir, "english");
            _stemmer = new EnglishSnowballStemmer();
        }
    }

    public SparseVector Embed(string text)
    {
        var document = Bm25Tokenizer.RemoveNonAlphanumeric(text);
        var tokens = Bm25Tokenizer.Tokenize(document);
        var stemmed = Stem(tokens);
        if (stemmed.Count == 0)
        {
            return new SparseVector(Array.Empty<uint>(), Array.Empty<float>());
        }

        var tfMap = TermFrequency(stemmed);
        var indices = tfMap.Keys.OrderBy(k => k).Select(k => (uint)k).ToArray();
        var values = indices.Select(i => tfMap[(int)i]).ToArray();
        return new SparseVector(indices, values);
    }

    public IReadOnlyList<string> Stem(IReadOnlyList<string> tokens)
    {
        var stemmed = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            if (Punctuation.Contains(token))
            {
                continue;
            }

            var lower = token.ToLowerInvariant();
            if (_stopwords.Contains(lower))
            {
                continue;
            }

            if (token.Length > TokenMaxLength)
            {
                continue;
            }

            var stemmedToken = _stemmer.Stem(lower);
            if (!string.IsNullOrEmpty(stemmedToken))
            {
                stemmed.Add(stemmedToken);
            }
        }

        return stemmed;
    }

    private static Dictionary<int, float> TermFrequency(IReadOnlyList<string> tokens)
    {
        var counter = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            counter.TryGetValue(token, out var count);
            counter[token] = count + 1;
        }

        var docLen = tokens.Count;
        var tfMap = new Dictionary<int, float>();
        foreach (var (stemmedToken, numOccurrences) in counter)
        {
            var tokenId = Bm25MurmurHash3.ComputeTokenId(stemmedToken);
            var tf = numOccurrences * (K + 1);
            tf /= numOccurrences + K * (1 - B + B * docLen / AvgLen);
            tfMap[tokenId] = tf;
        }

        return tfMap;
    }

    private static HashSet<string> LoadStopwords(string modelDir, string language)
    {
        var path = Path.Combine(modelDir, $"{language}.txt");
        if (!File.Exists(path))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static FrozenSet<string> BuildPunctuationSet()
    {
        var punctuation = new HashSet<string>(StringComparer.Ordinal);
        for (var codePoint = 0; codePoint <= 0xFFFF; codePoint++)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory((char)codePoint);
            if (category is UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation)
            {
                punctuation.Add(((char)codePoint).ToString());
            }
        }

        return punctuation.ToFrozenSet(StringComparer.Ordinal);
    }
}
