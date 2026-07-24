using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Infrastructure.Indexing;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Golden and regression tests for tree-sitter chunking.</summary>
public sealed class ChunkerGoldenTests
{
    private static readonly string PySample = """
        def foo():
            return 1


        class Bar:
            def baz(self):
                return 2
        """;

    /// <summary>Chunk IDs are stable across repeated chunking of the same file.</summary>
    [Test]
    public async Task Chunk_ids_are_deterministic_for_python_sample()
    {
        var chunker = new TreeSitterChunker(
            Microsoft.Extensions.Options.Options.Create(TestSettingsFactory.CreateChunkingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TreeSitterChunker>.Instance);

        var first = chunker.ChunkFile("sample.py", PySample, SourceLanguage.Python, "deadbeef");
        var second = chunker.ChunkFile("sample.py", PySample, SourceLanguage.Python, "deadbeef");

        await Assert.That(second.Select(c => c.Id.Value).ToArray()).IsEquivalentTo(first.Select(c => c.Id.Value).ToArray());
        await Assert.That(first).Contains(c => c.SymbolName == "foo");
    }

    /// <summary>Python sample yields SymbolType and SourceLanguage enums (not string compares).</summary>
    [Test]
    public async Task Python_sample_yields_symbol_type_and_language_enums()
    {
        var chunker = new TreeSitterChunker(
            Microsoft.Extensions.Options.Options.Create(TestSettingsFactory.CreateChunkingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TreeSitterChunker>.Instance);

        var chunks = chunker.ChunkFile("sample.py", PySample, SourceLanguage.Python, "deadbeef");
        await Assert.That(chunks).IsNotEmpty();
        await Assert.That(chunks).All(c => c.Language == SourceLanguage.Python);
        await Assert.That(chunks).Contains(c => c.SymbolName == "foo" && c.SymbolType == SymbolType.Function);
        await Assert.That(chunks).Contains(c => c.SymbolName == "Bar" && c.SymbolType == SymbolType.Class);
    }

    /// <summary>Chunk IDs match the Python SHA-256 formula.</summary>
    [Test]
    public async Task Chunk_id_matches_python_sha256_formula()
    {
        var chunker = new TreeSitterChunker(
            Microsoft.Extensions.Options.Options.Create(TestSettingsFactory.CreateChunkingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TreeSitterChunker>.Instance);

        var chunks = chunker.ChunkFile("sample.py", PySample, SourceLanguage.Python, "deadbeef");
        await Assert.That(chunks).IsNotEmpty();
        foreach (var chunk in chunks)
        {
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{chunk.RelPath}:{chunk.StartLine}")))
                .ToLowerInvariant();
            await Assert.That(chunk.Id.Value).IsEquivalentTo(expected);
        }
    }

    /// <summary>Chunk IDs match expected values from the golden fixture.</summary>
    [Test]
    public async Task Golden_fixture_matches_expected_chunk_ids()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "chunk_id_golden.json");
        await Assert.That(File.Exists(path)).IsTrue().Because($"Missing fixture at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        await Assert.That(document.RootElement.TryGetProperty("samples", out var samples)).IsTrue();

        var chunker = new TreeSitterChunker(
            Microsoft.Extensions.Options.Options.Create(TestSettingsFactory.CreateChunkingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TreeSitterChunker>.Instance);

        foreach (var sample in samples.EnumerateArray())
        {
            var relPath = sample.GetProperty("rel_path").GetString()!;
            var languageWire = sample.GetProperty("language").GetString()!;
            await Assert.That(CodebaseIndexer.Domain.Serialization.DomainEnumWire.TryParse(languageWire, out SourceLanguage language)).IsTrue();
            var fileSha256 = sample.GetProperty("file_sha256").GetString()!;
            var content = sample.GetProperty("content").GetString()!;
            var expected = sample.GetProperty("expected_chunk_ids")
                .EnumerateArray()
                .Select(e => e.GetString()!)
                .ToArray();

            var chunks = chunker.ChunkFile(relPath, content, language, fileSha256);
            await Assert.That(chunks.Select(c => c.Id.Value).ToArray()).IsEquivalentTo(expected);
        }
    }

    /// <summary>Golden fixture file exists and contains samples.</summary>
    [Test]
    public async Task Golden_fixture_file_exists()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "chunk_id_golden.json");
        await Assert.That(File.Exists(path)).IsTrue().Because($"Missing fixture at {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        await Assert.That(document.RootElement.TryGetProperty("samples", out _)).IsTrue();
    }

    /// <summary>SQL procedure regex fallback extracts procedure symbol names.</summary>
    [Test]
    public async Task Sql_procedure_regex_fallback_extracts_procedure()
    {
        const string sql = """
            CREATE PROCEDURE dbo.MyProc
            AS
            BEGIN
                SELECT 1;
            END
            """;
        var chunker = new TreeSitterChunker(
            Microsoft.Extensions.Options.Options.Create(TestSettingsFactory.CreateChunkingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TreeSitterChunker>.Instance);
        var chunks = chunker.ChunkFile("proc.sql", sql, SourceLanguage.Sql, "abc");
        await Assert.That(chunks).Contains(c => c.SymbolName == "dbo.MyProc");
    }

    /// <summary>C# files use TreeSitter.DotNet language id C# (c-sharp native lib).</summary>
    [Test]
    public async Task Csharp_sample_produces_class_and_method_chunks()
    {
        const string csharp = """
            using System;

            namespace Demo;

            public sealed class Widget
            {
                public int Add(int left, int right) => left + right;
            }
            """;
        var chunker = new TreeSitterChunker(
            Microsoft.Extensions.Options.Options.Create(TestSettingsFactory.CreateChunkingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TreeSitterChunker>.Instance);
        var chunks = chunker.ChunkFile("Widget.cs", csharp, SourceLanguage.CSharp, "abc");
        await Assert.That(chunks).Contains(c => c.SymbolName == "Widget" && c.SymbolType == SymbolType.Class);
        await Assert.That(chunks).All(c => c.Language == SourceLanguage.CSharp);
    }
}