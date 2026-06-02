# src/codebase_indexer/tools/chunk.py
"""MCP tool: get_chunk"""

from fastmcp import FastMCP

from codebase_indexer.config import Settings
from codebase_indexer.storage.qdrant import QdrantStorage


def register_chunk_tool(mcp: FastMCP, settings: Settings, storage: QdrantStorage) -> None:
    @mcp.tool(
        name="get_chunk",
        description="Retrieve a specific chunk by ID from a prior search result.",
    )
    async def get_chunk(
        chunk_id: str,
        collection: str | None = None,
    ) -> dict:
        coll = collection or settings.qdrant_collection
        result = await storage.get_chunk_by_id(coll, chunk_id)
        if result is None:
            return {"error": f"Chunk '{chunk_id}' not found in collection '{coll}'."}
        return result
