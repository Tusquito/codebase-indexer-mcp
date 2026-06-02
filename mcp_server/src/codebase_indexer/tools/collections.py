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
                "embed_model": s.embed_model,
                "hybrid": s.hybrid,
            }
            for s in stats
        ]
