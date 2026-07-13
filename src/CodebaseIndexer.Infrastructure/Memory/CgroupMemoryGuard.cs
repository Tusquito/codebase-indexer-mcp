using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Infrastructure.Memory;

/// <summary>Reads cgroup memory limit and usage from the Linux filesystem.</summary>
public static class CgroupMemoryGuard
{
    private const long UsageCacheMilliseconds = 1_000;

    private static long? _cachedLimit;
    private static long? _cachedUsage;
    private static long _cachedUsageAtMs;

    /// <summary>Gets the cgroup memory limit in bytes, or null when unlimited or unavailable.</summary>
    /// <returns>Memory limit in bytes, or null.</returns>
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

    /// <summary>Gets current cgroup memory usage in bytes with a short-lived cache.</summary>
    /// <returns>Memory usage in bytes, or null when unavailable.</returns>
    public static long? GetCgroupMemoryUsageBytes()
    {
        var now = Environment.TickCount64;
        if (_cachedUsage.HasValue && now - _cachedUsageAtMs < UsageCacheMilliseconds)
        {
            return _cachedUsage;
        }

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
                    _cachedUsage = usage;
                    _cachedUsageAtMs = now;
                    return usage;
                }
            }
            catch (Exception)
            {
                // try next path
            }
        }

        _cachedUsage = null;
        _cachedUsageAtMs = now;
        return null;
    }

    /// <summary>Computes memory pressure as a percentage of the cgroup limit.</summary>
    /// <returns>Usage percentage (0–100), or 0 when limit or usage is unknown.</returns>
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

    /// <summary>Evaluates memory pressure against warn and halt thresholds.</summary>
    /// <param name="warnPct">Percentage at or above which severity is <see cref="MemoryPressureSeverity.Warn"/>.</param>
    /// <param name="haltPct">Percentage at or above which severity is <see cref="MemoryPressureSeverity.Halt"/>.</param>
    /// <returns>Severity and current usage percentage.</returns>
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
