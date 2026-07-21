namespace CodebaseIndexer.Application.Options;

/// <summary>Configuration for optional Neo4j GraphRAG.</summary>
public sealed class GraphOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Graph";

    /// <summary>Master switch for Neo4j I/O and expand_search_context registration.</summary>
    public bool Enabled { get; init; }

    /// <summary>Bolt URI for Neo4j.</summary>
    public string Neo4jUri { get; init; } = string.Empty;

    /// <summary>Neo4j auth user.</summary>
    public string Neo4jUser { get; init; } = string.Empty;

    /// <summary>Neo4j auth password (required when Enabled).</summary>
    public string Neo4jPassword { get; init; } = string.Empty;

    /// <summary>Neo4j database name.</summary>
    public string Neo4jDatabase { get; init; } = string.Empty;

    /// <summary>Cypher UNWIND batch size for graph writes.</summary>
    public int WriterBatch { get; init; }

    /// <summary>Maximum hop count for expand_search_context.</summary>
    public int MaxHops { get; init; }

    /// <summary>Maximum nodes returned by expand_search_context.</summary>
    public int MaxNodes { get; init; }
}
