namespace CodebaseIndexer.Domain.Models;

/// <summary>Lifecycle status of an indexing job.</summary>
public enum IndexJobStatus
{
    /// <summary>Job is queued and has not started yet.</summary>
    Queued,

    /// <summary>Job is actively indexing files.</summary>
    Running,

    /// <summary>Job finished successfully.</summary>
    Done,

    /// <summary>Job terminated due to an unrecoverable error.</summary>
    Failed,

    /// <summary>Job was cancelled before completion.</summary>
    Cancelled,
}
