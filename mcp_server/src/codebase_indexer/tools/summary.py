# src/codebase_indexer/tools/summary.py
"""MCP tool: get_collection_summary — compact codebase orientation in one call."""

from collections import Counter

from fastmcp import FastMCP

from codebase_indexer.config import Settings
from codebase_indexer.storage.qdrant import QdrantStorage


def _top_level_dirs(rel_paths: list[str], depth: int = 2) -> list[str]:
    """Extract unique directory prefixes up to `depth` segments."""
    seen: set[str] = set()
    for path in rel_paths:
        parts = path.replace("\\", "/").split("/")
        # Build prefixes up to the requested depth (skip the filename)
        dir_parts = parts[:-1]
        for d in range(1, min(depth + 1, len(dir_parts) + 1)):
            prefix = "/".join(dir_parts[:d])
            if prefix:
                seen.add(prefix)
    return sorted(seen)


def register_collection_summary_tool(
    mcp: FastMCP, settings: Settings, storage: QdrantStorage
) -> None:
    @mcp.tool(
        name="get_collection_summary",
        description=(
            "Compact codebase orientation in a single tool call — no embedding cost. "
            "Returns: file count per language, top-level directory tree (depth 2), "
            "symbol type breakdown, and the 10 most-chunked files. "
            "Call this first when entering an unfamiliar project to orient the AI "
            "before running searches. Replaces 3-5 exploratory search_codebase calls."
        ),
    )
    async def get_collection_summary(
        collection: str | None = None,
    ) -> dict:
        coll = collection or settings.qdrant_collection
        rows = await storage.scroll_all_payloads(coll)

        if not rows:
            return {
                "error": f"Collection '{coll}' is empty or does not exist.",
                "hint": "Use index_codebase to index a project first.",
            }

        # Aggregate per file
        files_by_path: dict[str, dict] = {}
        lang_counter: Counter = Counter()
        symbol_type_counter: Counter = Counter()
        chunks_per_file: Counter = Counter()

        for row in rows:
            rel_path = row["rel_path"]
            language = row["language"] or "unknown"
            symbol_type = row["symbol_type"] or "other"

            if rel_path not in files_by_path:
                files_by_path[rel_path] = {"language": language}
                lang_counter[language] += 1

            symbol_type_counter[symbol_type] += 1
            chunks_per_file[rel_path] += 1

        total_files = len(files_by_path)
        total_chunks = len(rows)
        all_paths = list(files_by_path.keys())

        top_dirs = _top_level_dirs(all_paths, depth=2)
        top_files = [
            {"rel_path": path, "chunk_count": count}
            for path, count in chunks_per_file.most_common(10)
        ]

        return {
            "collection": coll,
            "total_files": total_files,
            "total_chunks": total_chunks,
            "files_by_language": dict(lang_counter.most_common()),
            "symbols_by_type": dict(symbol_type_counter.most_common()),
            "directory_tree": top_dirs,
            "top_chunked_files": top_files,
        }
