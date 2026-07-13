namespace CodebaseIndexer.Domain.Models;

/// <summary>Severity level returned by a memory pressure guard.</summary>
public enum MemoryPressureSeverity
{
    /// <summary>Memory usage is within acceptable limits.</summary>
    Ok,

    /// <summary>Memory usage is elevated; indexing may slow down.</summary>
    Warn,

    /// <summary>Memory usage is critical; indexing should stop.</summary>
    Halt,
}
