# src/codebase_indexer/tools/outliers.py
"""MCP tool: find_outlier_chunks — semantic outlier discovery via Qdrant."""

from __future__ import annotations

from typing import TYPE_CHECKING

from fastmcp import FastMCP

if TYPE_CHECKING:
    from codebase_indexer.context import AppContext


def register_find_outlier_chunks_tool(mcp: FastMCP, ctx: "AppContext") -> None:
    settings = ctx.settings
    storage = ctx.storage

    @mcp.tool(
        name="find_outlier_chunks",
        description=(
            "Find code chunks semantically distant from a defined context "
            "(module path scope and/or explicit reference chunk IDs). "
            "Uses Qdrant Recommendation API with BEST_SCORE negative-only "
            "examples on the dense vector, then scores by cosine similarity "
            "to the context centroid (lower = more distant). "
            "Requires a single collection. "
            "Context is built from context_chunk_ids and/or a scroll sample "
            "(optional path_glob); whole-collection scan allowed when path_glob "
            "is omitted, bounded by OUTLIER_MAX_CONTEXT_SAMPLES. "
            "Chunks above max_similarity (default OUTLIER_MAX_SIMILARITY) are "
            "excluded. Missing context chunk IDs fail fast. "
            "limit is capped at 20. Gated by RECOMMEND_ENABLED. "
            "See docs/SEARCH_BEHAVIOR.md."
        ),
    )
    async def find_outlier_chunks(
        collection: str,
        context_chunk_ids: list[str] | None = None,
        limit: int = 5,
        language: str | None = None,
        path_glob: str | None = None,
        max_similarity: float | None = None,
        max_content_chars: int | None = None,
    ) -> dict:
        if limit > 20:
            limit = 20

        ctx_ids = context_chunk_ids or []
        if ctx_ids:
            await storage.verify_chunk_ids_exist(collection, ctx_ids)

        if max_similarity is not None and not (0.0 <= max_similarity <= 1.0):
            raise ValueError("max_similarity must be between 0.0 and 1.0.")

        effective_max_sim = (
            max_similarity
            if max_similarity is not None
            else settings.outlier_max_similarity
        )

        results = await storage.find_outlier_chunks(
            collection=collection,
            context_chunk_ids=ctx_ids or None,
            limit=limit,
            language=language,
            path_glob=path_glob,
            max_similarity=effective_max_sim,
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
                "similarity_to_context": round(r.score, 4),
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

        return {
            "results": result_items,
            "collection": collection,
            "context_examples": len(ctx_ids),
            "max_similarity": effective_max_sim,
        }
