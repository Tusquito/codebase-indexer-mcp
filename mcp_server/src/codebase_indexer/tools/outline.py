# src/codebase_indexer/tools/outline.py
"""MCP tool: get_file_outline — symbol tree for a file, no code content."""

from fastmcp import FastMCP

from codebase_indexer.config import Settings
from codebase_indexer.storage.qdrant import QdrantStorage


def register_file_outline_tool(
    mcp: FastMCP, settings: Settings, storage: QdrantStorage
) -> None:
    @mcp.tool(
        name="get_file_outline",
        description=(
            "Return the symbol tree of a specific file — no code content returned. "
            "Lists all symbols (classes, functions, methods, etc.) with their type "
            "and line numbers. Zero embedding cost: uses Qdrant payload scroll. "
            "Use to understand a file's structure before deciding which chunks to "
            "fetch with get_chunk. Saves tokens vs reading full file content."
        ),
    )
    async def get_file_outline(
        rel_path: str,
        collection: str | None = None,
    ) -> dict:
        coll = collection or settings.qdrant_collection
        symbols = await storage.scroll_file_symbols(coll, rel_path)

        if not symbols:
            return {
                "error": f"No symbols found for '{rel_path}' in collection '{coll}'.",
                "hint": "Check the path with search_symbols or list_collections.",
            }

        return {
            "collection": coll,
            "rel_path": rel_path,
            "symbol_count": len(symbols),
            "symbols": symbols,
        }
