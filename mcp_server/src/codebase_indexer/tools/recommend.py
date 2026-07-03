# src/codebase_indexer/tools/recommend.py
"""MCP tool: recommend_code — Qdrant Recommendation API on dense vectors."""

from __future__ import annotations

from typing import TYPE_CHECKING

from fastmcp import FastMCP

from codebase_indexer.telemetry.metrics import observe_tool

if TYPE_CHECKING:
    from codebase_indexer.context import AppContext


def register_recommend_tool(mcp: FastMCP, ctx: "AppContext") -> None:
    settings = ctx.settings
    storage = ctx.storage
    embedder = ctx.embedder

    @mcp.tool(
        name="recommend_code",
        description=(
            "Find code chunks similar to positive examples and dissimilar from "
            "negative examples using Qdrant's Recommendation API on the dense "
            "vector only (AVERAGE_VECTOR strategy). "
            "Provide positive examples via positive_chunk_ids and/or positive_query; "
            "optional negatives via negative_chunk_ids and/or negative_query. "
            "Requires a single collection (multi-collection search is not supported). "
            "Missing chunk IDs fail fast with an explicit error. "
            "path_glob is applied as a post-filter (fnmatch) with internal "
            "over-fetch limit*3. "
            "Example count (positive + negative) is capped by RECOMMEND_MAX_EXAMPLES. "
            "limit is capped at 20. See docs/SEARCH_BEHAVIOR.md."
        ),
    )
    @observe_tool("recommend_code")
    async def recommend_code(
        collection: str,
        positive_chunk_ids: list[str] | None = None,
        positive_query: str | None = None,
        negative_chunk_ids: list[str] | None = None,
        negative_query: str | None = None,
        limit: int = 5,
        language: str | None = None,
        path_glob: str | None = None,
        max_content_chars: int | None = None,
    ) -> dict:
        if limit > 20:
            limit = 20

        pos_ids = positive_chunk_ids or []
        neg_ids = negative_chunk_ids or []
        pos_query = (positive_query or "").strip()
        neg_query = (negative_query or "").strip()

        if not pos_ids and not pos_query:
            raise ValueError(
                "At least one positive example is required "
                "(positive_chunk_ids and/or positive_query)."
            )

        example_count = len(pos_ids) + len(neg_ids)
        if pos_query:
            example_count += 1
        if neg_query:
            example_count += 1
        if example_count > settings.recommend_max_examples:
            raise ValueError(
                f"Example count {example_count} exceeds RECOMMEND_MAX_EXAMPLES="
                f"{settings.recommend_max_examples} (positive + negative combined)."
            )

        all_chunk_ids = pos_ids + neg_ids
        if all_chunk_ids:
            await storage.verify_chunk_ids_exist(collection, all_chunk_ids)

        positive: list[str | list[float]] = [
            storage.chunk_id_to_point_id(cid) for cid in pos_ids
        ]
        negative: list[str | list[float]] = [
            storage.chunk_id_to_point_id(cid) for cid in neg_ids
        ]

        texts_to_embed: list[str] = []
        text_roles: list[str] = []
        if pos_query:
            texts_to_embed.append(pos_query)
            text_roles.append("positive")
        if neg_query:
            texts_to_embed.append(neg_query)
            text_roles.append("negative")

        if texts_to_embed:
            dense_vectors = await embedder.embed_batch_dense(texts_to_embed)
            for role, vec in zip(text_roles, dense_vectors, strict=True):
                if role == "positive":
                    positive.append(vec)
                else:
                    negative.append(vec)

        results = await storage.recommend(
            collection=collection,
            positive=positive,
            negative=negative or None,
            limit=limit,
            language=language,
            path_glob=path_glob,
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

        return {
            "results": result_items,
            "collection": collection,
            "positive_examples": len(positive),
            "negative_examples": len(negative),
        }
