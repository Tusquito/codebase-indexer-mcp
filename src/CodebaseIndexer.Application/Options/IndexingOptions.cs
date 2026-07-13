namespace CodebaseIndexer.Application.Options;

public sealed class IndexingOptions
{
    public bool HybridSearch { get; set; }
    public bool SequentialEmbed { get; set; }
    public int MemoryPressureWarnPct { get; set; }
    public int MemoryPressureHaltPct { get; set; }
    public bool ReleaseModelsAfterIndex { get; set; }
    public required string WorkspacePath { get; set; }
    public int FlushEvery { get; set; }
    public int UpsertBatch { get; set; }
}
