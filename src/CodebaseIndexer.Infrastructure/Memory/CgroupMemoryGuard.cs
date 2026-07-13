using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Infrastructure.Memory;

public static class CgroupMemoryGuard
{
    private static long? _cachedLimit;

    public static long? GetCgroupMemoryLimitBytes()
    {
        if (_cachedLimit.HasValue)
        {
            return _cachedLimit.Value == -1 ? null : _cachedLimit.Value;
        }

        foreach (var path in new[]
        {
            "/sys/fs/cgroup/memory.max",
            "/sys/fs/cgroup/memory/memory.limit_in_bytes",
        })
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var raw = File.ReadAllText(path).Trim();
                if (raw == "max" || raw == "9223372036854771712")
                {
                    _cachedLimit = -1;
                    return null;
                }

                if (long.TryParse(raw, out var limit))
                {
                    _cachedLimit = limit;
                    return limit;
                }
            }
            catch (Exception)
            {
                // try next path
            }
        }

        _cachedLimit = -1;
        return null;
    }

    public static long? GetCgroupMemoryUsageBytes()
    {
        foreach (var path in new[]
        {
            "/sys/fs/cgroup/memory.current",
            "/sys/fs/cgroup/memory/memory.usage_in_bytes",
        })
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var raw = File.ReadAllText(path).Trim();
                if (long.TryParse(raw, out var usage))
                {
                    return usage;
                }
            }
            catch (Exception)
            {
                // try next path
            }
        }

        return null;
    }

    public static double MemoryPressurePercent()
    {
        var limit = GetCgroupMemoryLimitBytes();
        if (limit is null or <= 0)
        {
            return 0;
        }

        var usage = GetCgroupMemoryUsageBytes();
        if (usage is null)
        {
            return 0;
        }

        return Math.Round(usage.Value / (double)limit.Value * 100, 1);
    }

    public static MemoryPressureResult CheckMemoryPressure(int warnPct, int haltPct)
    {
        var percent = MemoryPressurePercent();
        if (percent <= 0)
        {
            return new MemoryPressureResult(MemoryPressureSeverity.Ok, 0);
        }

        if (percent >= haltPct)
        {
            return new MemoryPressureResult(MemoryPressureSeverity.Halt, percent);
        }

        if (percent >= warnPct)
        {
            return new MemoryPressureResult(MemoryPressureSeverity.Warn, percent);
        }

        return new MemoryPressureResult(MemoryPressureSeverity.Ok, percent);
    }
}
