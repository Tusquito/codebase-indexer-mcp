using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for monitoring process memory usage during indexing.</summary>
public interface IMemoryPressureGuard
{
    /// <summary>Evaluates current memory usage against warn and halt thresholds.</summary>
    /// <param name="warnPct">Percentage at which a warning severity is returned.</param>
    /// <param name="haltPct">Percentage at which a halt severity is returned.</param>
    /// <returns>Current memory pressure severity and usage percentage.</returns>
    MemoryPressureResult Check(int warnPct, int haltPct);
}
