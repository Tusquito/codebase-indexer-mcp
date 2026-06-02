# src/codebase_indexer/tools/search.py
"""MCP tool: search_codebase"""

import asyncio
from collections import defaultdict

from fastmcp import FastMCP

from codebase_indexer.config import Settings
from codebase_indexer.indexer.embedder import Embedder
from codebase_indexer.storage.qdrant import QdrantStorage
from codebase_indexer.tools.cross_references import _classify_reference


def register_search_tool(
    mcp: FastMCP, settings: Settings, storage: QdrantStorage, embedder: Embedder
) -> None:
    @mcp.tool(
        name="search_codebase",
        description=(
            "Hybrid semantic + keyword search across indexed code. "
            "Combines dense vector similarity (nomic-embed-code) and "
            "BM25 keyword matching via RRF fusion. Returns code chunks "
            "only — no full files loaded. Token-efficient by design. "
            "'collection' should be set to the current project folder name "
            "(basename of the working directory). Pass additional project "
            "names in 'collections' to also search across other indexed projects. "
            "When searching multiple collections, results include 'cross_references' "
            "showing symbols that appear across collection boundaries (shared classes, "
            "interfaces, error codes, etc.). "
            "Set 'max_content_chars' to truncate chunk content in results and save "
            "tokens — use get_chunk to fetch full content of a specific chunk. "
            "'min_score' is a cosine threshold that only applies when hybrid search "
            "is disabled; in hybrid mode results are ranked by RRF fusion and bounded "
            "by 'top_k'."
        ),
    )
    async def search_codebase(
        query: str,
        top_k: int = 5,
        collection: str | None = None,
        collections: list[str] | None = None,
        language: str | None = None,
        min_score: float = 0.5,
        max_content_chars: int | None = None,
    ) -> dict:
        if top_k > 20:
            top_k = 20

        # Build the set of collections to search
        primary = collection or settings.qdrant_collection
        target_collections = [primary]
        if collections:
            for c in collections:
                if c not in target_collections:
                    target_collections.append(c)

        dense_vector, sparse_vector = await embedder.embed_query(query)

        # Search each collection (in parallel if multiple)
        if len(target_collections) == 1:
            results = await storage.search(
                collection=target_collections[0],
                dense_vector=dense_vector,
                sparse_vector=sparse_vector,
                top_k=top_k,
                language=language,
                min_score=min_score,
            )
        else:
            results = await storage.search(
                collection=None,  # None triggers cross-collection
                dense_vector=dense_vector,
                sparse_vector=sparse_vector,
                top_k=top_k,
                language=language,
                min_score=min_score,
                restrict_collections=target_collections,
            )

        result_items = []
        for r in results:
            content = r.content
            truncated = False
            if max_content_chars is not None and len(content) > max_content_chars:
                content = content[:max_content_chars]
                truncated = True
            item = {
                "chunk_id": r.chunk_id,
                "score": round(r.score, 4),
                "collection": r.collection,
                "rel_path": r.rel_path,
                "symbol_name": r.symbol_name,
                "symbol_type": r.symbol_type,
                "start_line": r.start_line,
                "end_line": r.end_line,
                "language": r.language,
                "content": content,
            }
            if truncated:
                item["content_truncated"] = True
            result_items.append(item)

        # Cross-reference detection: find symbols shared across collections
        cross_refs = []
        if len(target_collections) > 1:
            cross_refs = await _detect_cross_references(
                results, target_collections, storage
            )

        return {
            "results": result_items,
            "collections_searched": target_collections,
            "cross_references": cross_refs,
        }


async def _detect_cross_references(
    results: list,
    target_collections: list[str],
    storage: QdrantStorage,
) -> list[dict]:
    """Detect symbols that appear across collection boundaries.

    1. From search results, extract unique named symbols.
    2. For each symbol, check which target collections contain it.
    3. Return cross-reference entries for symbols found in 2+ collections.
    """
    # Collect unique symbols from results, tracking which collections they appeared in
    symbol_collections: dict[str, set[str]] = defaultdict(set)
    for r in results:
        if r.symbol_name:
            symbol_collections[r.symbol_name].add(r.collection)

    # For symbols found in only one collection, check others in parallel
    symbols_to_check: list[tuple[str, set[str]]] = []
    for sym, colls in symbol_collections.items():
        missing = set(target_collections) - colls
        if missing:
            symbols_to_check.append((sym, missing))

    if symbols_to_check:
        tasks = []
        for sym, missing_colls in symbols_to_check:
            tasks.append(
                storage.find_symbol_in_collections(sym, list(missing_colls), limit_per_collection=3)
            )
        found_results = await asyncio.gather(*tasks)
        for (sym, _), found in zip(symbols_to_check, found_results):
            for r in found:
                symbol_collections[sym].add(r.collection)

    # Build cross-reference entries for symbols in 2+ collections
    cross_refs = []
    for sym, colls in symbol_collections.items():
        if len(colls) >= 2:
            # Get file locations per collection with reference classification
            locations: dict[str, list[dict]] = defaultdict(list)
            for r in results:
                if r.symbol_name == sym:
                    entry = {
                        "path": f"{r.rel_path}:{r.start_line}",
                        "reference_type": _classify_reference(r.content, sym),
                    }
                    if entry not in locations[r.collection]:
                        locations[r.collection].append(entry)

            cross_refs.append({
                "symbol": sym,
                "collections": sorted(colls),
                "locations": dict(locations),
            })

    cross_refs.sort(key=lambda x: len(x["collections"]), reverse=True)
    return cross_refs
