namespace CodebaseIndexer.Domain.Models;

public enum MemoryPressureSeverity
{
    Ok,
    Warn,
    Halt,
}

public sealed record MemoryPressureResult(MemoryPressureSeverity Severity, double Percent);
