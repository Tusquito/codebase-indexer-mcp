using System.Text.Json.Serialization;
using CodebaseIndexer.Domain.Results;

namespace CodebaseIndexer.Application.Models;

/// <summary>Unified MCP tool failure envelope (ADR 0033 Phase 3).</summary>
public sealed record McpErrorEnvelope(
    [property: JsonPropertyName("error")] McpErrorBody Error);
