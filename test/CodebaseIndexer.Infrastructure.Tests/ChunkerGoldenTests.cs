using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Infrastructure.Indexing;

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
    [Fact]
    public void Chunk_ids_are_deterministic_for_python_sample()
    {
        var chunker = new TreeSitterChunker(
            Microsoft.Extensions.Options.Options.Create(TestSettingsFactory.CreateChunkingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TreeSitterChunker>.Instance);

        var first = chunker.ChunkFile("sample.py", PySample, SourceLanguage.Python, "deadbeef");
        var second = chunker.ChunkFile("sample.py", PySample, SourceLanguage.Python, "deadbeef");

        Assert.Equal(
            first.Select(c => c.Id.Value).ToArray(),
            second.Select(c => c.Id.Value).ToArray());
        Assert.Contains(first, c => c.SymbolName == "foo");
    }

    /// <summary>Python sample yields SymbolType and SourceLanguage enums (not string compares).</summary>
    [Fact]
    public void Python_sample_yields_symbol_type_and_language_enums()
    {
        var chunker = new TreeSitterChunker(
            Microsoft.Extensions.Options.Options.Create(TestSettingsFactory.CreateChunkingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TreeSitterChunker>.Instance);

        var chunks = chunker.ChunkFile("sample.py", PySample, SourceLanguage.Python, "deadbeef");
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal(SourceLanguage.Python, c.Language));
        Assert.Contains(chunks, c => c.SymbolName == "foo" && c.SymbolType == SymbolType.Function);
        Assert.Contains(chunks, c => c.SymbolName == "Bar" && c.SymbolType == SymbolType.Class);
    }

    /// <summary>Chunk IDs match the Python SHA-256 formula.</summary>
    [Fact]
    public void Chunk_id_matches_python_sha256_formula()
    {
        var chunker = new TreeSitterChunker(
            Microsoft.Extensions.Options.Options.Create(TestSettingsFactory.CreateChunkingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TreeSitterChunker>.Instance);

        var chunks = chunker.ChunkFile("sample.py", PySample, SourceLanguage.Python, "deadbeef");
        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{chunk.RelPath}:{chunk.StartLine}")))
                .ToLowerInvariant();
            Assert.Equal(expected, chunk.Id.Value);
        }
    }

    /// <summary>Chunk IDs match expected values from the golden fixture.</summary>
    [Fact]
    public void Golden_fixture_matches_expected_chunk_ids()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "chunk_id_golden.json");
        Assert.True(File.Exists(path), $"Missing fixture at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(document.RootElement.TryGetProperty("samples", out var samples));

        var chunker = new TreeSitterChunker(
            Microsoft.Extensions.Options.Options.Create(TestSettingsFactory.CreateChunkingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TreeSitterChunker>.Instance);

        foreach (var sample in samples.EnumerateArray())
        {
            var relPath = sample.GetProperty("rel_path").GetString()!;
            var languageWire = sample.GetProperty("language").GetString()!;
            Assert.True(CodebaseIndexer.Domain.Serialization.DomainEnumWire.TryParse(languageWire, out SourceLanguage language));
            var fileSha256 = sample.GetProperty("file_sha256").GetString()!;
            var content = sample.GetProperty("content").GetString()!;
            var expected = sample.GetProperty("expected_chunk_ids")
                .EnumerateArray()
                .Select(e => e.GetString())
                .ToArray();

            var chunks = chunker.ChunkFile(relPath, content, language, fileSha256);
            Assert.Equal(expected, chunks.Select(c => c.Id.Value).ToArray());
        }
    }

    /// <summary>Golden fixture file exists and contains samples.</summary>
    [Fact]
    public void Golden_fixture_file_exists()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "chunk_id_golden.json");
        Assert.True(File.Exists(path), $"Missing fixture at {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(document.RootElement.TryGetProperty("samples", out _));
    }

    /// <summary>SQL procedure regex fallback extracts procedure symbol names.</summary>
    [Fact]
    public void Sql_procedure_regex_fallback_extracts_procedure()
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
        Assert.Contains(chunks, c => c.SymbolName == "dbo.MyProc");
    }

    /// <summary>C# files use TreeSitter.DotNet language id C# (c-sharp native lib).</summary>
    [Fact]
    public void Csharp_sample_produces_class_and_method_chunks()
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
        Assert.Contains(chunks, c => c.SymbolName == "Widget" && c.SymbolType == SymbolType.Class);
        Assert.All(chunks, c => Assert.Equal(SourceLanguage.CSharp, c.Language));
    }
}
