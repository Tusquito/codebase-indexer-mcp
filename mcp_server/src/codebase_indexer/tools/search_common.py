# src/codebase_indexer/tools/search_common.py
"""Shared collection-resolution and search-dispatch helpers.

Used by both search_codebase and search_symbols, which previously duplicated
the target-collection assembly and the single-vs-multi search branch.
"""

from __future__ import annotations

from codebase_indexer.indexer.embedder import Embedder, SparseVector
from codebase_indexer.storage.qdrant import QdrantStorage, SearchResult
from codebase_indexer.telemetry.metrics import record_search_results


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
