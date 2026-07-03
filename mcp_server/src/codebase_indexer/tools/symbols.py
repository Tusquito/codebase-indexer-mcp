# src/codebase_indexer/tools/symbols.py
"""MCP tool: search_symbols — symbol-only search with zero code content."""

from __future__ import annotations

from typing import TYPE_CHECKING

from fastmcp import FastMCP

from codebase_indexer.tools.search_common import resolve_collections, run_search

if TYPE_CHECKING:
    from codebase_indexer.context import AppContext


def register_search_symbols_tool(mcp: FastMCP, ctx: "AppContext") -> None:
    settings = ctx.settings
    storage = ctx.storage
    embedder = ctx.embedder

    @mcp.tool(
        name="search_symbols",
        description=(
            "This tool caps top_k at 30. When HYBRID_SEARCH is enabled (default), "
            "results are ranked by reciprocal rank fusion — min_score is ignored. "
            "When HYBRID_SEARCH is disabled, only dense cosine search runs and "
            "min_score filters by similarity. See docs/SEARCH_BEHAVIOR.md. "
            "Token-efficient symbol lookup: runs the same hybrid search as "
            "search_codebase but returns ONLY symbol metadata — no code content. "
            "Returns: chunk_id, rel_path, symbol_name, symbol_type, start_line, "
            "end_line, language, score, collection. "
            "Use when you only need to know WHERE a symbol is defined/used, "
            "not what its code looks like. Call get_chunk for full content "
            "of any specific result. Saves ~90% tokens vs search_codebase "
            "for orientation and symbol-location tasks. "
            "When RERANK_ENABLED=true, pass rerank=false to skip ColBERT "
            "query embed and MAX_SIM rerank (hybrid RRF only)."
        ),
    )
    async def search_symbols(
        query: str,
        top_k: int = 10,
        collection: str | None = None,
        collections: list[str] | None = None,
        language: str | None = None,
        min_score: float = 0.4,
        rerank: bool | None = None,
    ) -> dict:
        if top_k > 30:
            top_k = 30

        target_collections = resolve_collections(
            collection or settings.qdrant_collection, collections
        )
        results = await run_search(
            storage,
            embedder,
            query,
            target_collections,
            top_k,
            language,
            min_score,
            rerank=rerank,
        )

        return {
            "results": [
                {
                    "chunk_id": r.chunk_id,
                    "score": round(r.score, 4),
                    "collection": r.collection,
                    "rel_path": r.rel_path,
                    "symbol_name": r.symbol_name,
                    "symbol_type": r.symbol_type,
                    "start_line": r.start_line,
                    "end_line": r.end_line,
                    "language": r.language,
                }
                for r in results
            ],
            "collections_searched": target_collections,
        }
