using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;

namespace CodebaseIndexer.Infrastructure.Memory;

/// <summary><see cref="IMemoryPressureGuard"/> implementation using Linux cgroup memory metrics.</summary>
public sealed class CgroupMemoryPressureGuard : IMemoryPressureGuard
{
    /// <inheritdoc />
    public MemoryPressureResult Check(int warnPct, int haltPct) =>
        CgroupMemoryGuard.CheckMemoryPressure(warnPct, haltPct);
}
