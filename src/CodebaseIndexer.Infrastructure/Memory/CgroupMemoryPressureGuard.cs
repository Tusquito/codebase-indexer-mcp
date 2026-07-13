using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;

namespace CodebaseIndexer.Infrastructure.Memory;

public sealed class CgroupMemoryPressureGuard : IMemoryPressureGuard
{
    public MemoryPressureResult Check(int warnPct, int haltPct) =>
        CgroupMemoryGuard.CheckMemoryPressure(warnPct, haltPct);
}
