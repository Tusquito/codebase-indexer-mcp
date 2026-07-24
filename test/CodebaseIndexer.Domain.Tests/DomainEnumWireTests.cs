using System.Text.Json;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Serialization;
using DomainMatchType = CodebaseIndexer.Domain.Models.MatchType;
using System.Threading.Tasks;

namespace CodebaseIndexer.Domain.Tests;

/// <summary>Wire-string and JSON round-trip coverage for domain closed-set enums.</summary>
public sealed class DomainEnumWireTests
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    /// <summary>Every <see cref="SymbolType"/> member maps to its canonical wire form.</summary>
    [Test]
    [Arguments(SymbolType.Function, "function")]
    [Arguments(SymbolType.Class, "class")]
    [Arguments(SymbolType.Method, "method")]
    [Arguments(SymbolType.Other, "other")]
    [Arguments(SymbolType.Type, "type")]
    [Arguments(SymbolType.Config, "config")]
    [Arguments(SymbolType.Manifest, "manifest")]
    [Arguments(SymbolType.Ops, "ops")]
    [Arguments(SymbolType.Table, "table")]
    [Arguments(SymbolType.Procedure, "procedure")]
    [Arguments(SymbolType.View, "view")]
    [Arguments(SymbolType.Trigger, "trigger")]
    [Arguments(SymbolType.Index, "index")]
    public async Task SymbolType_round_trips_wire(SymbolType value, string wire)
    {
        await Assert.That(DomainEnumWire.ToWire(value)).IsEqualTo(wire);
        await Assert.That(DomainEnumWire.TryParse(wire, out SymbolType parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(value);
        await Assert.That(JsonSerializer.Serialize(value, JsonOptions).Trim('"')).IsEqualTo(wire);
        await Assert.That(JsonSerializer.Deserialize<SymbolType>($"\"{wire}\"", JsonOptions)).IsEqualTo(value);
    }

    /// <summary>Every <see cref="SourceLanguage"/> member maps to its canonical wire form.</summary>
    [Test]
    [Arguments(SourceLanguage.Unknown, "unknown")]
    [Arguments(SourceLanguage.Python, "python")]
    [Arguments(SourceLanguage.JavaScript, "javascript")]
    [Arguments(SourceLanguage.TypeScript, "typescript")]
    [Arguments(SourceLanguage.Go, "go")]
    [Arguments(SourceLanguage.Rust, "rust")]
    [Arguments(SourceLanguage.Java, "java")]
    [Arguments(SourceLanguage.C, "c")]
    [Arguments(SourceLanguage.Cpp, "cpp")]
    [Arguments(SourceLanguage.CSharp, "csharp")]
    [Arguments(SourceLanguage.Sql, "sql")]
    [Arguments(SourceLanguage.Xml, "xml")]
    [Arguments(SourceLanguage.Yaml, "yaml")]
    [Arguments(SourceLanguage.Json, "json")]
    [Arguments(SourceLanguage.Properties, "properties")]
    [Arguments(SourceLanguage.Toml, "toml")]
    [Arguments(SourceLanguage.Hcl, "hcl")]
    [Arguments(SourceLanguage.Dockerfile, "dockerfile")]
    [Arguments(SourceLanguage.Groovy, "groovy")]
    [Arguments(SourceLanguage.Protobuf, "protobuf")]
    [Arguments(SourceLanguage.Kotlin, "kotlin")]
    [Arguments(SourceLanguage.Scala, "scala")]
    [Arguments(SourceLanguage.Ruby, "ruby")]
    [Arguments(SourceLanguage.Php, "php")]
    [Arguments(SourceLanguage.Swift, "swift")]
    [Arguments(SourceLanguage.Dart, "dart")]
    [Arguments(SourceLanguage.Bash, "bash")]
    [Arguments(SourceLanguage.PowerShell, "powershell")]
    [Arguments(SourceLanguage.Markdown, "markdown")]
    public async Task SourceLanguage_round_trips_wire(SourceLanguage value, string wire)
    {
        await Assert.That(DomainEnumWire.ToWire(value)).IsEqualTo(wire);
        await Assert.That(DomainEnumWire.TryParse(wire, out SourceLanguage parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(value);
        await Assert.That(JsonSerializer.Serialize(value, JsonOptions).Trim('"')).IsEqualTo(wire);
        await Assert.That(JsonSerializer.Deserialize<SourceLanguage>($"\"{wire}\"", JsonOptions)).IsEqualTo(value);
    }

    /// <summary>Every <see cref="NamedVector"/> member maps to its canonical Qdrant wire form.</summary>
    [Test]
    [Arguments(NamedVector.Dense, "dense")]
    [Arguments(NamedVector.Sparse, "sparse")]
    [Arguments(NamedVector.Colbert, "colbert")]
    public async Task NamedVector_round_trips_wire(NamedVector value, string wire)
    {
        await Assert.That(DomainEnumWire.ToWire(value)).IsEqualTo(wire);
        await Assert.That(DomainEnumWire.TryParse(wire, out NamedVector parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(value);
        await Assert.That(JsonSerializer.Serialize(value, JsonOptions).Trim('"')).IsEqualTo(wire);
        await Assert.That(JsonSerializer.Deserialize<NamedVector>($"\"{wire}\"", JsonOptions)).IsEqualTo(value);
    }

    /// <summary>Every <see cref="DomainMatchType"/> member maps to its canonical wire form.</summary>
    [Test]
    [Arguments(DomainMatchType.Semantic, "semantic")]
    [Arguments(DomainMatchType.ExactSymbol, "exact_symbol")]
    [Arguments(DomainMatchType.ImportSearch, "import_search")]
    [Arguments(DomainMatchType.CallSite, "call_site")]
    public async Task MatchType_round_trips_wire(DomainMatchType value, string wire)
    {
        await Assert.That(DomainEnumWire.ToWire(value)).IsEqualTo(wire);
        await Assert.That(DomainEnumWire.TryParse(wire, out DomainMatchType parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(value);
        await Assert.That(JsonSerializer.Serialize(value, JsonOptions).Trim('"')).IsEqualTo(wire);
        await Assert.That(JsonSerializer.Deserialize<DomainMatchType>($"\"{wire}\"", JsonOptions)).IsEqualTo(value);
    }

    /// <summary>Every <see cref="ReferenceType"/> member maps to its canonical wire form.</summary>
    [Test]
    [Arguments(ReferenceType.Definition, "definition")]
    [Arguments(ReferenceType.Import, "import")]
    [Arguments(ReferenceType.Usage, "usage")]
    [Arguments(ReferenceType.EndpointDefinition, "endpoint_definition")]
    [Arguments(ReferenceType.HttpCall, "http_call")]
    [Arguments(ReferenceType.CallSite, "call_site")]
    [Arguments(ReferenceType.ServiceConfig, "service_config")]
    [Arguments(ReferenceType.BuildDependency, "build_dependency")]
    public async Task ReferenceType_round_trips_wire(ReferenceType value, string wire)
    {
        await Assert.That(DomainEnumWire.ToWire(value)).IsEqualTo(wire);
        await Assert.That(DomainEnumWire.TryParse(wire, out ReferenceType parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(value);
        await Assert.That(JsonSerializer.Serialize(value, JsonOptions).Trim('"')).IsEqualTo(wire);
        await Assert.That(JsonSerializer.Deserialize<ReferenceType>($"\"{wire}\"", JsonOptions)).IsEqualTo(value);
    }

    /// <summary>Every <see cref="LivenessStatus"/> member maps to its canonical wire form.</summary>
    [Test]
    [Arguments(LivenessStatus.Ok, "ok")]
    [Arguments(LivenessStatus.Unhealthy, "unhealthy")]
    public async Task LivenessStatus_round_trips_wire(LivenessStatus value, string wire)
    {
        await Assert.That(DomainEnumWire.ToWire(value)).IsEqualTo(wire);
        await Assert.That(DomainEnumWire.TryParse(wire, out LivenessStatus parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(value);
        await Assert.That(JsonSerializer.Serialize(value, JsonOptions).Trim('"')).IsEqualTo(wire);
        await Assert.That(JsonSerializer.Deserialize<LivenessStatus>($"\"{wire}\"", JsonOptions)).IsEqualTo(value);
    }

    /// <summary>Unknown symbol wires fall back to <see cref="SymbolType.Other"/>.</summary>
    [Test]
    public async Task ParseOrOther_falls_back_to_other()
    {
        await Assert.That(DomainEnumWire.ParseOrOther(null)).IsEqualTo(SymbolType.Other);
        await Assert.That(DomainEnumWire.ParseOrOther("")).IsEqualTo(SymbolType.Other);
        await Assert.That(DomainEnumWire.ParseOrOther("methode")).IsEqualTo(SymbolType.Other);
    }

    /// <summary>Unknown language wires fall back to <see cref="SourceLanguage.Unknown"/>.</summary>
    [Test]
    public async Task ParseOrUnknown_falls_back_to_unknown()
    {
        await Assert.That(DomainEnumWire.ParseOrUnknown(null)).IsEqualTo(SourceLanguage.Unknown);
        await Assert.That(DomainEnumWire.ParseOrUnknown("")).IsEqualTo(SourceLanguage.Unknown);
        await Assert.That(DomainEnumWire.ParseOrUnknown("fortran")).IsEqualTo(SourceLanguage.Unknown);
    }

    /// <summary><see cref="Chunk"/> defaults <see cref="SymbolType"/> to Other.</summary>
    [Test]
    public async Task Chunk_defaults_symbol_type_to_other()
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
        await Assert.That(chunk.SymbolType).IsEqualTo(SymbolType.Other);
    }

    /// <summary>DomainEnumWire covers every SymbolType and SourceLanguage member.</summary>
    [Test]
    public async Task DomainEnumWire_covers_all_symbol_and_language_members()
    {
        foreach (SymbolType value in Enum.GetValues<SymbolType>())
        {
            var wire = DomainEnumWire.ToWire(value);
            await Assert.That(string.IsNullOrWhiteSpace(wire)).IsFalse();
            await Assert.That(DomainEnumWire.TryParse(wire, out SymbolType parsed)).IsTrue();
            await Assert.That(parsed).IsEqualTo(value);
        }

        foreach (SourceLanguage value in Enum.GetValues<SourceLanguage>())
        {
            var wire = DomainEnumWire.ToWire(value);
            await Assert.That(string.IsNullOrWhiteSpace(wire)).IsFalse();
            await Assert.That(DomainEnumWire.TryParse(wire, out SourceLanguage parsed)).IsTrue();
            await Assert.That(parsed).IsEqualTo(value);
        }
    }

    /// <summary>DomainEnumWire covers every <see cref="NamedVector"/> member.</summary>
    [Test]
    public async Task DomainEnumWire_covers_all_NamedVector_members()
    {
        foreach (NamedVector value in Enum.GetValues<NamedVector>())
        {
            var wire = DomainEnumWire.ToWire(value);
            await Assert.That(string.IsNullOrWhiteSpace(wire)).IsFalse();
            await Assert.That(DomainEnumWire.TryParse(wire, out NamedVector parsed)).IsTrue();
            await Assert.That(parsed).IsEqualTo(value);
        }
    }

    /// <summary>DomainEnumWire covers every MatchType, ReferenceType, and LivenessStatus member.</summary>
    [Test]
    public async Task DomainEnumWire_covers_all_phase3_members()
    {
        foreach (DomainMatchType value in Enum.GetValues<DomainMatchType>())
        {
            var wire = DomainEnumWire.ToWire(value);
            await Assert.That(string.IsNullOrWhiteSpace(wire)).IsFalse();
            await Assert.That(DomainEnumWire.TryParse(wire, out DomainMatchType parsed)).IsTrue();
            await Assert.That(parsed).IsEqualTo(value);
        }

        foreach (ReferenceType value in Enum.GetValues<ReferenceType>())
        {
            var wire = DomainEnumWire.ToWire(value);
            await Assert.That(string.IsNullOrWhiteSpace(wire)).IsFalse();
            await Assert.That(DomainEnumWire.TryParse(wire, out ReferenceType parsed)).IsTrue();
            await Assert.That(parsed).IsEqualTo(value);
        }

        foreach (LivenessStatus value in Enum.GetValues<LivenessStatus>())
        {
            var wire = DomainEnumWire.ToWire(value);
            await Assert.That(string.IsNullOrWhiteSpace(wire)).IsFalse();
            await Assert.That(DomainEnumWire.TryParse(wire, out LivenessStatus parsed)).IsTrue();
            await Assert.That(parsed).IsEqualTo(value);
        }
    }
}