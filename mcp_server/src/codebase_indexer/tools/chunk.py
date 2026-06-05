# src/codebase_indexer/tools/chunk.py
"""MCP tool: get_chunk"""

from __future__ import annotations

from typing import TYPE_CHECKING

from fastmcp import FastMCP

if TYPE_CHECKING:
    from codebase_indexer.context import AppContext


def register_chunk_tool(mcp: FastMCP, ctx: "AppContext") -> None:
    storage = ctx.storage

    @mcp.tool(
        name="get_chunk",
        description="Retrieve a specific chunk by ID from a prior search result.",
    )
    async def get_chunk(
        chunk_id: str,
        collection: str | None = None,
    ) -> dict:
        result = await storage.find_chunk_by_id(chunk_id, collection=collection)
        if result is None:
            scope = collection or "any collection"
            return {"error": f"Chunk '{chunk_id}' not found in {scope}."}
        return result
