"""Train/validation holdout split for golden-set fine-tuning."""

from __future__ import annotations

import random
from typing import Literal, Protocol, TypeVar

SplitStrategy = Literal["holdout_ids", "multi_hop", "stratified"]

# All multi_hop golden queries — fallback validation holdout (ADR 0020) used
# only when entries carry no ``tags`` metadata; the ``multi_hop`` strategy
# otherwise derives holdout ids dynamically from each entry's tags.
DEFAULT_HOLDOUT_IDS: frozenset[str] = frozenset(
    {
        "q_mh_reindex_pipeline",
        "q_mh_search_stack",
        "q_mh_workspace_collection",
        "q_mh_xref_service_map",
        "q_mh_build_deps",
        "q_mh_graph_pipeline",
        "q_mh_search_symbols",
        "q_mh_service_endpoints",
        "q_mh_tei_config",
        "q_mh_rerank_config",
        "q_mh_gpu_config",
        "q_mh_truncation_config",
        "q_mh_chunk_embed",
        "q_mh_scan_chunk",
        "q_mh_eval_harness",
        "q_mh_memory_trim",
    }
)

T = TypeVar("T", bound="HasQueryId")


class HasQueryId(Protocol):
    query_id: str


def split_holdout(
    entries: list[T],
    *,
    strategy: SplitStrategy = "multi_hop",
    holdout_ids: set[str] | frozenset[str] | None = None,
    seed: int = 42,
) -> tuple[list[T], list[T]]:
    """Split entries into (train, val) using the chosen holdout strategy.

    * ``holdout_ids`` — explicit query_id set for validation.
    * ``multi_hop`` — entries whose ``tags`` contain ``multi_hop`` go to val
      (falls back to ``DEFAULT_HOLDOUT_IDS`` when no tag metadata).
    * ``stratified`` — ~15% random holdout, seeded for reproducibility.
    """
    if not entries:
        return [], []

    if strategy == "holdout_ids":
        val_ids = set(holdout_ids or DEFAULT_HOLDOUT_IDS)
    elif strategy == "multi_hop":
        val_ids = _multi_hop_ids(entries, holdout_ids)
    elif strategy == "stratified":
        return _stratified_split(entries, seed=seed)
    else:
        raise ValueError(f"Unknown split strategy: {strategy!r}")

    train: list[T] = []
    val: list[T] = []
    for entry in entries:
        if entry.query_id in val_ids:
            val.append(entry)
        else:
            train.append(entry)
    return train, val


def _multi_hop_ids(
    entries: list[T],
    holdout_ids: set[str] | frozenset[str] | None,
) -> set[str]:
    if holdout_ids is not None:
        return set(holdout_ids)
    tagged = {
        e.query_id
        for e in entries
        if hasattr(e, "tags") and "multi_hop" in getattr(e, "tags", [])
    }
    return tagged if tagged else set(DEFAULT_HOLDOUT_IDS)


def _stratified_split(entries: list[T], *, seed: int) -> tuple[list[T], list[T]]:
    n = len(entries)
    holdout_count = max(1, round(n * 0.15))
    rng = random.Random(seed)
    indices = list(range(n))
    rng.shuffle(indices)
    val_idx = set(indices[:holdout_count])
    train = [e for i, e in enumerate(entries) if i not in val_idx]
    val = [e for i, e in enumerate(entries) if i in val_idx]
    return train, val
