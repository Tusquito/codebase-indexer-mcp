# src/codebase_indexer/tools/collections.py
"""MCP tool: list_collections"""

from __future__ import annotations

from typing import TYPE_CHECKING

from fastmcp import FastMCP

if TYPE_CHECKING:
    from codebase_indexer.context import AppContext


def register_collections_tool(mcp: FastMCP, ctx: "AppContext") -> None:
    storage = ctx.storage

    @mcp.tool(
        name="list_collections",
        description="List all indexed collections with statistics.",
    )
    async def list_collections() -> list[dict]:
        stats = await storage.list_collection_stats()
        return [
            {
                "name": s.name,
                "vector_count": s.vector_count,
                "disk_size_mb": s.disk_size_mb,
                "dense_embed_model": s.dense_embed_model,
                "sparse_embed_model": s.sparse_embed_model,
                "hybrid": s.hybrid,
                "rerank_enabled": s.rerank_enabled,
                "colbert_embed_model": s.colbert_embed_model or None,
            }
            for s in stats
        ]
