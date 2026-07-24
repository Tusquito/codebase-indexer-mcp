using System.Text.Json;
using CodebaseIndexer.Application.Mapping;
using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Unified MCP failure envelope wire shape (ADR 0033 Phase 3).</summary>
public sealed class McpErrorMapperTests
{
    [Test]
    public async Task FromError_serializes_pascal_case_kind_and_fields()
    {
        var metadata = new Dictionary<string, string> { ["hint"] = "try again" };
        var payload = McpErrorMapper.FromError(new Error(
            ErrorKind.Validation,
            McpErrorCodes.PathRequired,
            "Please specify a project folder to index.",
            metadata));

        var envelope = await Assert.That(payload).IsTypeOf<McpErrorEnvelope>();
        await Assert.That(envelope!.Error.Kind).IsEqualTo(ErrorKind.Validation);
        await Assert.That(envelope.Error.Code).IsEqualTo(McpErrorCodes.PathRequired);
        await Assert.That(envelope.Error.Message).IsEqualTo("Please specify a project folder to index.");
        await Assert.That(envelope.Error.Metadata!["hint"]).IsEqualTo("try again");

        var json = JsonSerializer.Serialize(envelope);
        using var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("error");
        await Assert.That(error.GetProperty("kind").GetString()).IsEqualTo("Validation");
        await Assert.That(error.GetProperty("code").GetString()).IsEqualTo(McpErrorCodes.PathRequired);
        await Assert.That(error.GetProperty("message").GetString())
            .IsEqualTo("Please specify a project folder to index.");
        await Assert.That(error.GetProperty("metadata").GetProperty("hint").GetString()).IsEqualTo("try again");
        await Assert.That(json.Contains("\"kind\":\"validation\"", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task FromError_omits_null_metadata()
    {
        var payload = McpErrorMapper.FromError(new Error(
            ErrorKind.NotFound,
            IndexErrorCodes.JobNotFound,
            "Job missing"));

        var envelope = await Assert.That(payload).IsTypeOf<McpErrorEnvelope>();
        var json = JsonSerializer.Serialize(envelope);
        using var doc = JsonDocument.Parse(json);
        await Assert.That(doc.RootElement.GetProperty("error").GetProperty("kind").GetString())
            .IsEqualTo("NotFound");
        await Assert.That(doc.RootElement.GetProperty("error").TryGetProperty("metadata", out _)).IsFalse();
    }
}
