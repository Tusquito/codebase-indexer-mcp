using CodebaseIndexer.Domain.Ports;

namespace CodebaseIndexer.Infrastructure.Memory;

public sealed class CgroupMemoryPressureGuard : IMemoryPressureGuard
{
    public (Domain.Ports.MemoryPressureSeverity Severity, double Percent) Check(int warnPct, int haltPct)
    {
        var (severity, pct) = CgroupMemoryGuard.CheckMemoryPressure(warnPct, haltPct);
        return severity switch
        {
            MemoryPressureSeverity.Halt => (Domain.Ports.MemoryPressureSeverity.Halt, pct),
            MemoryPressureSeverity.Warn => (Domain.Ports.MemoryPressureSeverity.Warn, pct),
            _ => (Domain.Ports.MemoryPressureSeverity.Ok, pct),
        };
    }
}
