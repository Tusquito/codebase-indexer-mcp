"""Application telemetry (ADR 0018)."""

from codebase_indexer.telemetry.metrics import (
    init_metrics,
    metrics_enabled,
    observe_tool,
    record_embed_request,
    record_index_job,
    record_memory_pressure,
    record_search_results,
    record_truncated_chunks,
)

__all__ = [
    "init_metrics",
    "metrics_enabled",
    "observe_tool",
    "record_embed_request",
    "record_index_job",
    "record_memory_pressure",
    "record_search_results",
    "record_truncated_chunks",
]
