# src/codebase_indexer/tools/search_common.py
"""Shared collection-resolution and search-dispatch helpers.

Used by both search_codebase and search_symbols, which previously duplicated
the target-collection assembly and the single-vs-multi search branch.
"""

from __future__ import annotations

import structlog

from codebase_indexer.indexer.embedder import Embedder, SparseVector
from codebase_indexer.storage.qdrant import QdrantStorage, SearchResult
from codebase_indexer.telemetry.metrics import record_search_results

log = structlog.get_logger()

# Collections already warned about missing graph linkage — warn only once each
# so opt-in GRAPH_ENABLED users aren't spammed on every query.
_warned_unlinked_collections: set[str] = set()


async def warn_if_graph_linkage_missing(
    storage: QdrantStorage, collections: list[str]
) -> None:
    """Warn once per collection that lacks graph_node_ids linkage.

    Only fires when GRAPH_ENABLED=true; a linked collection is one whose Qdrant
    metadata carries ``graph_enabled=true`` (stamped after a graph-enabled
    index run). An unlinked collection means it was indexed before Phase 2 and
    should be re-indexed to populate ``graph_node_ids``.
    """
    if not storage.settings.graph_enabled:
        return
    for coll in collections:
        if coll in _warned_unlinked_collections:
            continue
        try:
            linked = await storage.collection_has_graph_enabled(coll)
        except Exception:
            continue
        if not linked:
            _warned_unlinked_collections.add(coll)
            log.warning("graph_linkage_missing", collection=coll)


def resolve_collections(primary: str, collections: list[str] | None) -> list[str]:
    """Build the de-duplicated list of collections to search (primary first)."""
    target = [primary]
    if collections:
        for c in collections:
            if c not in target:
                target.append(c)
    return target


async def dispatch_search(
    storage: QdrantStorage,
    dense_vector: list[float],
    sparse_vector: SparseVector | None,
    colbert_vector: list[list[float]] | None,
    target_collections: list[str],
    top_k: int,
    language: str | None,
    min_score: float,
) -> list[SearchResult]:
    """Search one or many collections with pre-computed query vectors."""
    if len(target_collections) == 1:
        return await storage.search(
            collection=target_collections[0],
            dense_vector=dense_vector,
            sparse_vector=sparse_vector,
            colbert_vector=colbert_vector,
            top_k=top_k,
            language=language,
            min_score=min_score,
        )
    return await storage.search(
        collection=None,
        dense_vector=dense_vector,
        sparse_vector=sparse_vector,
        colbert_vector=colbert_vector,
        top_k=top_k,
        language=language,
        min_score=min_score,
        restrict_collections=target_collections,
    )


async def run_search(
    storage: QdrantStorage,
    embedder: Embedder,
    query: str,
    target_collections: list[str],
    top_k: int,
    language: str | None,
    min_score: float,
    rerank: bool | None = None,
) -> list[SearchResult]:
    """Embed the query and search one or many collections."""
    dense_vector, sparse_vector, colbert_vector = await embedder.embed_query(
        query, rerank=rerank
    )
    await warn_if_graph_linkage_missing(storage, target_collections)
    results = await dispatch_search(
        storage,
        dense_vector,
        sparse_vector,
        colbert_vector,
        target_collections,
        top_k,
        language,
        min_score,
    )
    record_search_results(len(results), rerank=colbert_vector is not None)
    return results
