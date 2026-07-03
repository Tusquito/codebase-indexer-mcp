"""Prometheus application metrics (ADR 0018 Phase 1).

Idempotent init guarded by METRICS_ENABLED (default false). When disabled,
all record helpers and observe_tool are no-ops with near-zero overhead.
"""

from __future__ import annotations

import functools
import time
from collections.abc import Awaitable, Callable
from typing import Any, ParamSpec, TypeVar

P = ParamSpec("P")
R = TypeVar("R")

_enabled = False
_initialized = False
_registry: Any = None

_mcp_tool_requests: Any = None
_mcp_tool_duration: Any = None
_search_results: Any = None
_index_jobs: Any = None
_index_duration: Any = None
_index_chunks: Any = None
_embed_requests: Any = None
_memory_pressure: Any = None
_truncated_chunks: Any = None


def metrics_enabled() -> bool:
    return _enabled


def init_metrics(enabled: bool) -> None:
    """Register Prometheus collectors once per process."""
    global _enabled, _initialized, _registry
    global _mcp_tool_requests, _mcp_tool_duration, _search_results
    global _index_jobs, _index_duration, _index_chunks, _embed_requests
    global _memory_pressure, _truncated_chunks

    if _initialized:
        return
    _initialized = True
    _enabled = enabled
    if not enabled:
        return

    from prometheus_client import (
        GC_COLLECTOR,
        PLATFORM_COLLECTOR,
        PROCESS_COLLECTOR,
        CollectorRegistry,
        Counter,
        Histogram,
    )

    _registry = CollectorRegistry(auto_describe=True)
    _registry.register(PROCESS_COLLECTOR)
    _registry.register(PLATFORM_COLLECTOR)
    _registry.register(GC_COLLECTOR)

    _mcp_tool_requests = Counter(
        "codeindexer_mcp_tool_requests_total",
        "MCP tool invocations",
        ["tool", "status"],
        registry=_registry,
    )
    _mcp_tool_duration = Histogram(
        "codeindexer_mcp_tool_duration_seconds",
        "MCP tool latency in seconds",
        ["tool"],
        buckets=(0.01, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0, 30.0, 60.0, 120.0, 300.0),
        registry=_registry,
    )
    _search_results = Histogram(
        "codeindexer_search_results",
        "Number of search hits returned per query",
        ["rerank"],
        buckets=(0, 1, 2, 3, 5, 10, 15, 20, 30, 50),
        registry=_registry,
    )
    _index_jobs = Counter(
        "codeindexer_index_jobs_total",
        "Index job completions by terminal status",
        ["status"],
        registry=_registry,
    )
    _index_duration = Histogram(
        "codeindexer_index_duration_seconds",
        "Index job wall time in seconds",
        buckets=(1.0, 5.0, 15.0, 30.0, 60.0, 120.0, 300.0, 600.0, 1800.0, 3600.0),
        registry=_registry,
    )
    _index_chunks = Counter(
        "codeindexer_index_chunks_total",
        "Chunks embedded and upserted during indexing",
        registry=_registry,
    )
    _embed_requests = Counter(
        "codeindexer_embed_requests_total",
        "Embedding backend HTTP / ONNX calls",
        ["backend", "status"],
        registry=_registry,
    )
    _memory_pressure = Counter(
        "codeindexer_memory_pressure_events_total",
        "Cgroup memory pressure threshold crossings",
        ["severity"],
        registry=_registry,
    )
    _truncated_chunks = Counter(
        "codeindexer_truncated_chunks_total",
        "Text chunks truncated before embedding",
        ["backend"],
        registry=_registry,
    )


def render_metrics() -> tuple[bytes, str]:
    """Return (body, content_type) for GET /metrics."""
    from prometheus_client import CONTENT_TYPE_LATEST, generate_latest

    if _registry is None:
        return b"", CONTENT_TYPE_LATEST
    return generate_latest(_registry), CONTENT_TYPE_LATEST


def observe_tool(tool_name: str) -> Callable[[Callable[P, Awaitable[R]]], Callable[P, Awaitable[R]]]:
    """Metrics-only decorator for MCP tool handlers (traces come from FastMCP)."""

    def decorator(fn: Callable[P, Awaitable[R]]) -> Callable[P, Awaitable[R]]:
        @functools.wraps(fn)
        async def wrapper(*args: P.args, **kwargs: P.kwargs) -> R:
            if not _enabled or _mcp_tool_requests is None:
                return await fn(*args, **kwargs)
            start = time.monotonic()
            status = "success"
            try:
                return await fn(*args, **kwargs)
            except Exception:
                status = "error"
                raise
            finally:
                elapsed = time.monotonic() - start
                _mcp_tool_requests.labels(tool=tool_name, status=status).inc()
                _mcp_tool_duration.labels(tool=tool_name).observe(elapsed)

        return wrapper

    return decorator


def record_search_results(count: int, rerank: bool) -> None:
    if not _enabled or _search_results is None:
        return
    label = "true" if rerank else "false"
    _search_results.labels(rerank=label).observe(count)


def record_index_job(status: str, duration_seconds: float, chunks: int) -> None:
    if not _enabled or _index_jobs is None:
        return
    _index_jobs.labels(status=status).inc()
    if duration_seconds > 0 and _index_duration is not None:
        _index_duration.observe(duration_seconds)
    if chunks > 0 and _index_chunks is not None:
        _index_chunks.inc(chunks)


def record_embed_request(backend: str, status: str) -> None:
    if not _enabled or _embed_requests is None:
        return
    _embed_requests.labels(backend=backend, status=status).inc()


def record_memory_pressure(severity: str) -> None:
    if not _enabled or _memory_pressure is None or severity not in ("warn", "halt"):
        return
    _memory_pressure.labels(severity=severity).inc()


def record_truncated_chunks(backend: str, count: int = 1) -> None:
    if not _enabled or _truncated_chunks is None or count <= 0:
        return
    _truncated_chunks.labels(backend=backend).inc(count)
