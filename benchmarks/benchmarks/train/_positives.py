"""Resolve labeled positive passage text from indexed Qdrant chunks."""

from __future__ import annotations

from typing import TYPE_CHECKING

from benchmarks.eval_retrieval import GoldenEntry, resolve_labels

if TYPE_CHECKING:
    from codebase_indexer.storage.qdrant import QdrantStorage


async def resolve_positive_passage(
    storage: QdrantStorage,
    entry: GoldenEntry,
    *,
    collection: str | None = None,
) -> str:
    """Return passage text for the highest-grade labeled chunk.

    Uses ``resolve_labels`` to merge explicit chunk_id labels and
    ``rel_path:start_line`` aliases, then fetches ``content`` from Qdrant.
    """
    labels = resolve_labels(entry)
    if not labels:
        raise ValueError(f"No labels for query {entry.query_id!r}")

    best_grade = max(labels.values())
    best_ids = [cid for cid, grade in labels.items() if grade == best_grade]

    coll = collection or entry.collection
    for chunk_id in best_ids:
        payload = await storage.get_chunk_by_id(coll, chunk_id)
        if payload is None:
            continue
        content = payload.get("content", "").strip()
        if content:
            return content

    raise LookupError(
        f"Could not resolve positive passage for {entry.query_id!r} "
        f"in collection {coll!r} (tried {len(best_ids)} chunk_id(s))"
    )
