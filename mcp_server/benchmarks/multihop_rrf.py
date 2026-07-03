"""Client-side RRF fusion for multi-hop search evaluation (ADR 0009)."""

from __future__ import annotations

from codebase_indexer.storage.qdrant import SearchResult


def fuse_hop_rrf(
    hop_results: list[list[SearchResult]],
    *,
    rrf_k: int,
    top_k: int,
) -> list[SearchResult]:
    """Fuse ranked lists from multiple search hops with RRF on ``chunk_id``.

    Each hop contributes ``1 / (rrf_k + rank)`` per ``chunk_id``. Ties break
    on ``chunk_id`` for deterministic ordering.
    """
    fused_scores: dict[str, float] = {}
    by_chunk: dict[str, SearchResult] = {}

    for hop in hop_results:
        for rank, result in enumerate(hop, start=1):
            cid = result.chunk_id
            fused_scores[cid] = fused_scores.get(cid, 0.0) + 1.0 / (rrf_k + rank)
            by_chunk.setdefault(cid, result)

    ranked = sorted(
        fused_scores,
        key=lambda cid: (fused_scores[cid], cid),
        reverse=True,
    )
    return [by_chunk[cid] for cid in ranked[:top_k]]
