# src/codebase_indexer/memory.py
"""Cgroup-aware memory utilities for OOM prevention.

Reads the effective memory limit and current usage from cgroup v2 (or v1
fallback) so the indexing pipeline can throttle or abort before the kernel
kills the container silently.

All functions are safe to call from any thread (including ONNX thread-pool
workers).  The cgroup limit is cached on first read — it never changes for
a running container.
"""

import functools
import gc
import logging
import os

_log = logging.getLogger(__name__)


@functools.lru_cache(maxsize=1)
def get_cgroup_memory_limit() -> int | None:
    """Return the container's hard memory limit in bytes, or None if unknown.

    Checks cgroups v2 first (``memory.max``), then v1
    (``memory.limit_in_bytes``).  Returns ``None`` on non-Linux hosts or
    when the cgroup files are absent / unreadable (e.g. local dev on macOS
    or Windows).
    """
    for path in (
        "/sys/fs/cgroup/memory.max",
        "/sys/fs/cgroup/memory/memory.limit_in_bytes",
    ):
        try:
            raw = open(path).read().strip()
            if raw == "max" or raw == "9223372036854771712":
                # No limit imposed (unlimited cgroup / host-level).
                return None
            return int(raw)
        except (FileNotFoundError, PermissionError, ValueError, OSError):
            continue
    return None


def get_cgroup_memory_usage() -> int | None:
    """Return the current cgroup memory usage in bytes, or None if unknown.

    Checks cgroups v2 first (``memory.current``), then v1
    (``memory.usage_in_bytes``).
    """
    for path in (
        "/sys/fs/cgroup/memory.current",
        "/sys/fs/cgroup/memory/memory.usage_in_bytes",
    ):
        try:
            return int(open(path).read().strip())
        except (FileNotFoundError, PermissionError, ValueError, OSError):
            continue
    return None


def get_process_rss_bytes() -> int:
    """Return current process RSS in bytes (Linux only, 0 elsewhere).

    Reads ``/proc/self/statm`` for a lightweight, thread-safe measurement.
    Field 1 (resident) is in pages; multiply by the page size.
    """
    try:
        with open("/proc/self/statm") as f:
            fields = f.read().split()
        page_size = os.sysconf("SC_PAGE_SIZE")  # type: ignore[attr-defined]
        return int(fields[1]) * page_size
    except Exception:
        return 0


def get_rss_mb() -> float:
    """Return current process RSS in MiB."""
    rss = get_process_rss_bytes()
    return round(rss / (1024 * 1024), 1) if rss else 0.0


def memory_pressure_pct() -> float:
    """Return cgroup memory usage as a percentage of the limit.

    Returns 0.0 when the limit or usage cannot be determined (e.g. local dev
    outside a container).
    """
    limit = get_cgroup_memory_limit()
    if not limit:
        return 0.0
    usage = get_cgroup_memory_usage()
    if usage is None:
        return 0.0
    return round(usage / limit * 100, 1)


def check_memory_pressure(
    warn_pct: int = 70,
    halt_pct: int = 85,
) -> tuple[str, float]:
    """Check memory pressure and return a severity level.

    Returns:
        ("ok", pct)   — below warn threshold
        ("warn", pct) — between warn and halt thresholds
        ("halt", pct) — above halt threshold (caller should abort)
    """
    pct = memory_pressure_pct()
    if pct <= 0:
        return ("ok", 0.0)
    if pct >= halt_pct:
        return ("halt", pct)
    if pct >= warn_pct:
        return ("warn", pct)
    return ("ok", pct)


def emergency_trim() -> None:
    """Aggressive memory reclamation: GC + malloc_trim."""
    gc.collect()
    try:
        import ctypes
        ctypes.CDLL("libc.so.6").malloc_trim(0)
    except Exception:
        pass


def log_memory_diagnostics(context: str = "startup") -> None:
    """Log memory configuration and usage for diagnostics."""
    limit = get_cgroup_memory_limit()
    usage = get_cgroup_memory_usage()
    rss = get_process_rss_bytes()
    pct = memory_pressure_pct()

    limit_mb = round(limit / (1024 * 1024), 1) if limit else None
    usage_mb = round(usage / (1024 * 1024), 1) if usage else None
    rss_mb = round(rss / (1024 * 1024), 1) if rss else None

    _log.info(
        "memory_diagnostics context=%s cgroup_limit_mb=%s cgroup_usage_mb=%s "
        "process_rss_mb=%s pressure_pct=%s",
        context, limit_mb, usage_mb, rss_mb, pct,
    )
