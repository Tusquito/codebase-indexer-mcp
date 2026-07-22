using System.Diagnostics;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Benchmarks;

/// <summary>Minimal search latency harness (report-only; no Docker).</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var iterations = 1_000;
        if (args.Length > 0 && int.TryParse(args[0], out var parsed))
        {
            iterations = parsed;
        }

        var listA = Enumerable.Range(0, 20)
            .Select(i => new SearchHit(
                new ChunkId($"a-{i}"),
                1.0 - i * 0.01,
                $"a/{i}.cs",
                SourceLanguage.CSharp,
                1,
                10,
                $"Sym{i}",
                SymbolType.Method,
                "body",
                "coll-a"))
            .ToArray();
        var listB = Enumerable.Range(0, 20)
            .Select(i => new SearchHit(
                new ChunkId($"b-{i}"),
                1.0 - i * 0.01,
                $"b/{i}.cs",
                SourceLanguage.CSharp,
                1,
                10,
                $"Sym{i}",
                SymbolType.Method,
                "body",
                "coll-b"))
            .ToArray();

        // Warmup
        _ = CrossCollectionRrf.Fuse([listA, listB], rrfK: 60, topK: 10);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            _ = CrossCollectionRrf.Fuse([listA, listB], rrfK: 60, topK: 10);
        }

        sw.Stop();
        var perCallMs = sw.Elapsed.TotalMilliseconds / iterations;
        Console.WriteLine($"cross_collection_rrf iterations={iterations} avg_ms={perCallMs:F4}");
        return 0;
    }
}
