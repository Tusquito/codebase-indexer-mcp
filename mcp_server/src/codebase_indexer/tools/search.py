# src/codebase_indexer/tools/search.py
"""MCP tool: search_codebase"""

from __future__ import annotations

import asyncio
from collections import defaultdict
from typing import TYPE_CHECKING

from fastmcp import FastMCP

from codebase_indexer.storage.qdrant import QdrantStorage
from codebase_indexer.tools.cross_references import UrlExtractors
from codebase_indexer.tools.search_common import resolve_collections, run_search

if TYPE_CHECKING:
    from codebase_indexer.context import AppContext


def register_search_tool(mcp: FastMCP, ctx: "AppContext") -> None:
    settings = ctx.settings
    storage = ctx.storage
    embedder = ctx.embedder
    extractors = ctx.url_extractors

    @mcp.tool(
        name="search_codebase",
        description=(
            "This tool caps top_k at 20. When HYBRID_SEARCH is enabled (default), "
            "results are ranked by reciprocal rank fusion — min_score is ignored. "
            "When HYBRID_SEARCH is disabled, only dense cosine search runs and "
            "min_score filters by similarity. See docs/SEARCH_BEHAVIOR.md. "
            "Hybrid semantic + keyword search across indexed code. "
            "Combines dense vector similarity (OLLAMA_EMBED_MODEL via Ollama) and "
            "sparse matching (SPARSE_EMBED_MODEL) via RRF fusion. Returns code chunks "
            "only — no full files loaded. Token-efficient by design. "
            "'collection' should be set to the current project folder name "
            "(basename of the working directory). Pass additional project "
            "names in 'collections' to also search across other indexed projects. "
            "When searching multiple collections, results include 'cross_references' "
            "showing symbols that appear across collection boundaries (shared classes, "
            "interfaces, error codes, etc.). "
            "Set 'max_content_chars' to truncate chunk content in results and save "
            "tokens — use get_chunk to fetch full content of a specific chunk."
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

        target_collections = resolve_collections(
            collection or settings.qdrant_collection, collections
        )
        results = await run_search(
            storage, embedder, query, target_collections, top_k, language, min_score
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
                results, target_collections, storage, extractors
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
    extractors: UrlExtractors,
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
                        "reference_type": extractors.classify_reference(r.content, sym),
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
