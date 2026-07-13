namespace CodebaseIndexer.Domain.Models;

/// <summary>Result of a memory pressure check.</summary>
/// <param name="Severity">Severity level relative to configured thresholds.</param>
/// <param name="Percent">Current memory usage percentage.</param>
public sealed record MemoryPressureResult(MemoryPressureSeverity Severity, double Percent);
