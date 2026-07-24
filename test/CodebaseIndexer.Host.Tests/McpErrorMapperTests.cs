using System.Text.Json;
using CodebaseIndexer.Application.Mapping;
using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Unified MCP failure envelope wire shape (ADR 0033 Phase 3).</summary>
public sealed class McpErrorMapperTests
{
    [Fact]
    public void FromError_serializes_pascal_case_kind_and_fields()
    {
        var metadata = new Dictionary<string, string> { ["hint"] = "try again" };
        var payload = McpErrorMapper.FromError(new Error(
            ErrorKind.Validation,
            McpErrorCodes.PathRequired,
            "Please specify a project folder to index.",
            metadata));

        var envelope = Assert.IsType<McpErrorEnvelope>(payload);
        Assert.Equal(ErrorKind.Validation, envelope.Error.Kind);
        Assert.Equal(McpErrorCodes.PathRequired, envelope.Error.Code);
        Assert.Equal("Please specify a project folder to index.", envelope.Error.Message);
        Assert.Equal("try again", envelope.Error.Metadata!["hint"]);

        var json = JsonSerializer.Serialize(envelope);
        using var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("Validation", error.GetProperty("kind").GetString());
        Assert.Equal(McpErrorCodes.PathRequired, error.GetProperty("code").GetString());
        Assert.Equal("Please specify a project folder to index.", error.GetProperty("message").GetString());
        Assert.Equal("try again", error.GetProperty("metadata").GetProperty("hint").GetString());
        Assert.DoesNotContain("\"kind\":\"validation\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FromError_omits_null_metadata()
    {
        var payload = McpErrorMapper.FromError(new Error(
            ErrorKind.NotFound,
            IndexErrorCodes.JobNotFound,
            "Job missing"));

        var json = JsonSerializer.Serialize(Assert.IsType<McpErrorEnvelope>(payload));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("NotFound", doc.RootElement.GetProperty("error").GetProperty("kind").GetString());
        Assert.False(doc.RootElement.GetProperty("error").TryGetProperty("metadata", out _));
    }
}
