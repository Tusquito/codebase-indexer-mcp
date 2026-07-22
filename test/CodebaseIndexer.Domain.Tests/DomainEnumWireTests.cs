using System.Text.Json;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Serialization;
using DomainMatchType = CodebaseIndexer.Domain.Models.MatchType;

namespace CodebaseIndexer.Domain.Tests;

/// <summary>Wire-string and JSON round-trip coverage for domain closed-set enums.</summary>
public sealed class DomainEnumWireTests
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    /// <summary>Every <see cref="SymbolType"/> member maps to its canonical wire form.</summary>
    [Theory]
    [InlineData(SymbolType.Function, "function")]
    [InlineData(SymbolType.Class, "class")]
    [InlineData(SymbolType.Method, "method")]
    [InlineData(SymbolType.Other, "other")]
    [InlineData(SymbolType.Type, "type")]
    [InlineData(SymbolType.Config, "config")]
    [InlineData(SymbolType.Manifest, "manifest")]
    [InlineData(SymbolType.Ops, "ops")]
    [InlineData(SymbolType.Table, "table")]
    [InlineData(SymbolType.Procedure, "procedure")]
    [InlineData(SymbolType.View, "view")]
    [InlineData(SymbolType.Trigger, "trigger")]
    [InlineData(SymbolType.Index, "index")]
    public void SymbolType_round_trips_wire(SymbolType value, string wire)
    {
        Assert.Equal(wire, DomainEnumWire.ToWire(value));
        Assert.True(DomainEnumWire.TryParse(wire, out SymbolType parsed));
        Assert.Equal(value, parsed);
        Assert.Equal(wire, JsonSerializer.Serialize(value, JsonOptions).Trim('"'));
        Assert.Equal(value, JsonSerializer.Deserialize<SymbolType>($"\"{wire}\"", JsonOptions));
    }

    /// <summary>Every <see cref="SourceLanguage"/> member maps to its canonical wire form.</summary>
    [Theory]
    [InlineData(SourceLanguage.Unknown, "unknown")]
    [InlineData(SourceLanguage.Python, "python")]
    [InlineData(SourceLanguage.JavaScript, "javascript")]
    [InlineData(SourceLanguage.TypeScript, "typescript")]
    [InlineData(SourceLanguage.Go, "go")]
    [InlineData(SourceLanguage.Rust, "rust")]
    [InlineData(SourceLanguage.Java, "java")]
    [InlineData(SourceLanguage.C, "c")]
    [InlineData(SourceLanguage.Cpp, "cpp")]
    [InlineData(SourceLanguage.CSharp, "csharp")]
    [InlineData(SourceLanguage.Sql, "sql")]
    [InlineData(SourceLanguage.Xml, "xml")]
    [InlineData(SourceLanguage.Yaml, "yaml")]
    [InlineData(SourceLanguage.Json, "json")]
    [InlineData(SourceLanguage.Properties, "properties")]
    [InlineData(SourceLanguage.Toml, "toml")]
    [InlineData(SourceLanguage.Hcl, "hcl")]
    [InlineData(SourceLanguage.Dockerfile, "dockerfile")]
    [InlineData(SourceLanguage.Groovy, "groovy")]
    [InlineData(SourceLanguage.Protobuf, "protobuf")]
    [InlineData(SourceLanguage.Kotlin, "kotlin")]
    [InlineData(SourceLanguage.Scala, "scala")]
    [InlineData(SourceLanguage.Ruby, "ruby")]
    [InlineData(SourceLanguage.Php, "php")]
    [InlineData(SourceLanguage.Swift, "swift")]
    [InlineData(SourceLanguage.Dart, "dart")]
    [InlineData(SourceLanguage.Bash, "bash")]
    [InlineData(SourceLanguage.PowerShell, "powershell")]
    [InlineData(SourceLanguage.Markdown, "markdown")]
    public void SourceLanguage_round_trips_wire(SourceLanguage value, string wire)
    {
        Assert.Equal(wire, DomainEnumWire.ToWire(value));
        Assert.True(DomainEnumWire.TryParse(wire, out SourceLanguage parsed));
        Assert.Equal(value, parsed);
        Assert.Equal(wire, JsonSerializer.Serialize(value, JsonOptions).Trim('"'));
        Assert.Equal(value, JsonSerializer.Deserialize<SourceLanguage>($"\"{wire}\"", JsonOptions));
    }

    /// <summary>Every <see cref="NamedVector"/> member maps to its canonical Qdrant wire form.</summary>
    [Theory]
    [InlineData(NamedVector.Dense, "dense")]
    [InlineData(NamedVector.Sparse, "sparse")]
    [InlineData(NamedVector.Colbert, "colbert")]
    public void NamedVector_round_trips_wire(NamedVector value, string wire)
    {
        Assert.Equal(wire, DomainEnumWire.ToWire(value));
        Assert.True(DomainEnumWire.TryParse(wire, out NamedVector parsed));
        Assert.Equal(value, parsed);
        Assert.Equal(wire, JsonSerializer.Serialize(value, JsonOptions).Trim('"'));
        Assert.Equal(value, JsonSerializer.Deserialize<NamedVector>($"\"{wire}\"", JsonOptions));
    }

    /// <summary>Every <see cref="DomainMatchType"/> member maps to its canonical wire form.</summary>
    [Theory]
    [InlineData(DomainMatchType.Semantic, "semantic")]
    [InlineData(DomainMatchType.ExactSymbol, "exact_symbol")]
    [InlineData(DomainMatchType.ImportSearch, "import_search")]
    [InlineData(DomainMatchType.CallSite, "call_site")]
    public void MatchType_round_trips_wire(DomainMatchType value, string wire)
    {
        Assert.Equal(wire, DomainEnumWire.ToWire(value));
        Assert.True(DomainEnumWire.TryParse(wire, out DomainMatchType parsed));
        Assert.Equal(value, parsed);
        Assert.Equal(wire, JsonSerializer.Serialize(value, JsonOptions).Trim('"'));
        Assert.Equal(value, JsonSerializer.Deserialize<DomainMatchType>($"\"{wire}\"", JsonOptions));
    }

    /// <summary>Every <see cref="ReferenceType"/> member maps to its canonical wire form.</summary>
    [Theory]
    [InlineData(ReferenceType.Definition, "definition")]
    [InlineData(ReferenceType.Import, "import")]
    [InlineData(ReferenceType.Usage, "usage")]
    [InlineData(ReferenceType.EndpointDefinition, "endpoint_definition")]
    [InlineData(ReferenceType.HttpCall, "http_call")]
    [InlineData(ReferenceType.CallSite, "call_site")]
    [InlineData(ReferenceType.ServiceConfig, "service_config")]
    [InlineData(ReferenceType.BuildDependency, "build_dependency")]
    public void ReferenceType_round_trips_wire(ReferenceType value, string wire)
    {
        Assert.Equal(wire, DomainEnumWire.ToWire(value));
        Assert.True(DomainEnumWire.TryParse(wire, out ReferenceType parsed));
        Assert.Equal(value, parsed);
        Assert.Equal(wire, JsonSerializer.Serialize(value, JsonOptions).Trim('"'));
        Assert.Equal(value, JsonSerializer.Deserialize<ReferenceType>($"\"{wire}\"", JsonOptions));
    }

    /// <summary>Every <see cref="LivenessStatus"/> member maps to its canonical wire form.</summary>
    [Theory]
    [InlineData(LivenessStatus.Ok, "ok")]
    [InlineData(LivenessStatus.Unhealthy, "unhealthy")]
    public void LivenessStatus_round_trips_wire(LivenessStatus value, string wire)
    {
        Assert.Equal(wire, DomainEnumWire.ToWire(value));
        Assert.True(DomainEnumWire.TryParse(wire, out LivenessStatus parsed));
        Assert.Equal(value, parsed);
        Assert.Equal(wire, JsonSerializer.Serialize(value, JsonOptions).Trim('"'));
        Assert.Equal(value, JsonSerializer.Deserialize<LivenessStatus>($"\"{wire}\"", JsonOptions));
    }

    /// <summary>Unknown symbol wires fall back to <see cref="SymbolType.Other"/>.</summary>
    [Fact]
    public void ParseOrOther_falls_back_to_other()
    {
        Assert.Equal(SymbolType.Other, DomainEnumWire.ParseOrOther(null));
        Assert.Equal(SymbolType.Other, DomainEnumWire.ParseOrOther(""));
        Assert.Equal(SymbolType.Other, DomainEnumWire.ParseOrOther("methode"));
    }

    /// <summary>Unknown language wires fall back to <see cref="SourceLanguage.Unknown"/>.</summary>
    [Fact]
    public void ParseOrUnknown_falls_back_to_unknown()
    {
        Assert.Equal(SourceLanguage.Unknown, DomainEnumWire.ParseOrUnknown(null));
        Assert.Equal(SourceLanguage.Unknown, DomainEnumWire.ParseOrUnknown(""));
        Assert.Equal(SourceLanguage.Unknown, DomainEnumWire.ParseOrUnknown("fortran"));
    }

    /// <summary><see cref="Chunk"/> defaults <see cref="SymbolType"/> to Other.</summary>
    [Fact]
    public void Chunk_defaults_symbol_type_to_other()
    {
        var chunk = new Chunk(
            new ChunkId("a.py:1"),
            "a.py",
            "pass",
            1,
            1,
            null,
            SourceLanguage.Python,
            "sha");
        Assert.Equal(SymbolType.Other, chunk.SymbolType);
    }

    /// <summary>DomainEnumWire covers every SymbolType and SourceLanguage member.</summary>
    [Fact]
    public void DomainEnumWire_covers_all_symbol_and_language_members()
    {
        foreach (SymbolType value in Enum.GetValues<SymbolType>())
        {
            var wire = DomainEnumWire.ToWire(value);
            Assert.False(string.IsNullOrWhiteSpace(wire));
            Assert.True(DomainEnumWire.TryParse(wire, out SymbolType parsed));
            Assert.Equal(value, parsed);
        }

        foreach (SourceLanguage value in Enum.GetValues<SourceLanguage>())
        {
            var wire = DomainEnumWire.ToWire(value);
            Assert.False(string.IsNullOrWhiteSpace(wire));
            Assert.True(DomainEnumWire.TryParse(wire, out SourceLanguage parsed));
            Assert.Equal(value, parsed);
        }
    }

    /// <summary>DomainEnumWire covers every <see cref="NamedVector"/> member.</summary>
    [Fact]
    public void DomainEnumWire_covers_all_NamedVector_members()
    {
        foreach (NamedVector value in Enum.GetValues<NamedVector>())
        {
            var wire = DomainEnumWire.ToWire(value);
            Assert.False(string.IsNullOrWhiteSpace(wire));
            Assert.True(DomainEnumWire.TryParse(wire, out NamedVector parsed));
            Assert.Equal(value, parsed);
        }
    }

    /// <summary>DomainEnumWire covers every MatchType, ReferenceType, and LivenessStatus member.</summary>
    [Fact]
    public void DomainEnumWire_covers_all_phase3_members()
    {
        foreach (DomainMatchType value in Enum.GetValues<DomainMatchType>())
        {
            var wire = DomainEnumWire.ToWire(value);
            Assert.False(string.IsNullOrWhiteSpace(wire));
            Assert.True(DomainEnumWire.TryParse(wire, out DomainMatchType parsed));
            Assert.Equal(value, parsed);
        }

        foreach (ReferenceType value in Enum.GetValues<ReferenceType>())
        {
            var wire = DomainEnumWire.ToWire(value);
            Assert.False(string.IsNullOrWhiteSpace(wire));
            Assert.True(DomainEnumWire.TryParse(wire, out ReferenceType parsed));
            Assert.Equal(value, parsed);
        }

        foreach (LivenessStatus value in Enum.GetValues<LivenessStatus>())
        {
            var wire = DomainEnumWire.ToWire(value);
            Assert.False(string.IsNullOrWhiteSpace(wire));
            Assert.True(DomainEnumWire.TryParse(wire, out LivenessStatus parsed));
            Assert.Equal(value, parsed);
        }
    }
}
