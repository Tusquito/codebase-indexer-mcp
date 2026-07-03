# src/codebase_indexer/tools/summary.py
"""MCP tool: get_collection_summary — compact codebase orientation in one call."""

from __future__ import annotations

from collections import Counter
from typing import TYPE_CHECKING

from fastmcp import FastMCP

from codebase_indexer.tools.build_deps import (
    extract_build_deps,
    is_build_manifest,
    match_deps_to_collections,
)
from codebase_indexer.telemetry.metrics import observe_tool

if TYPE_CHECKING:
    from codebase_indexer.context import AppContext


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


def register_collection_summary_tool(mcp: FastMCP, ctx: "AppContext") -> None:
    settings = ctx.settings
    storage = ctx.storage

    @mcp.tool(
        name="get_collection_summary",
        description=(
            "Compact codebase orientation in a single tool call — no embedding cost. "
            "Returns: file count per language, top-level directory tree (depth 2), "
            "symbol type breakdown, the 10 most-chunked files, and build_dependencies "
            "listing which other indexed collections this project depends on at the "
            "package/build level (Maven, NuGet, npm, Gradle, Go, Cargo, Python). "
            "Call this first when entering an unfamiliar project to orient the AI "
            "before running searches. Replaces 3-5 exploratory search_codebase calls."
        ),
    )
    @observe_tool("get_collection_summary")
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
        manifest_paths: list[str] = []

        for row in rows:
            rel_path = row["rel_path"]
            language = row["language"] or "unknown"
            symbol_type = row["symbol_type"] or "other"

            if rel_path not in files_by_path:
                files_by_path[rel_path] = {"language": language}
                lang_counter[language] += 1
                if is_build_manifest(rel_path):
                    manifest_paths.append(rel_path)

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

        # Detect build dependencies against other indexed collections
        build_dependencies: list[dict] = []
        if manifest_paths:
            try:
                # Fetch other collections to match against
                all_stats = await storage.list_collection_stats()
                other_collections = [s.name for s in all_stats if s.name != coll]

                if other_collections:
                    # Fetch content for all manifest files in one scroll
                    manifest_chunks = await storage.scroll_chunks_by_paths(
                        coll,
                        manifest_paths,
                        payload_fields=["rel_path", "content"],
                    )

                    # Merge chunks per file, then extract + match deps
                    content_by_path: dict[str, str] = {}
                    for chunk in manifest_chunks:
                        p = chunk.get("rel_path", "")
                        content_by_path[p] = (
                            content_by_path.get(p, "") + "\n" + chunk.get("content", "")
                        )

                    seen_matches: set[str] = set()
                    for rel_path, content in content_by_path.items():
                        deps = extract_build_deps(content, rel_path)
                        matches = match_deps_to_collections(
                            deps, other_collections, self_collection=coll
                        )
                        for m in matches:
                            key = f"{m['artifact']}:{m['matched_collection']}"
                            if key not in seen_matches:
                                seen_matches.add(key)
                                build_dependencies.append({
                                    "artifact": m["artifact"],
                                    "group": m["group"],
                                    "version": m["version"],
                                    "scope": m["scope"],
                                    "ecosystem": m["ecosystem"],
                                    "matched_collection": m["matched_collection"],
                                    "match_confidence": m["match_confidence"],
                                    "declared_in": rel_path,
                                })
            except Exception:
                pass  # Non-critical — summary still useful without build dep info

        result: dict = {
            "collection": coll,
            "total_files": total_files,
            "total_chunks": total_chunks,
            "files_by_language": dict(lang_counter.most_common()),
            "symbols_by_type": dict(symbol_type_counter.most_common()),
            "directory_tree": top_dirs,
            "top_chunked_files": top_files,
        }
        if build_dependencies:
            result["build_dependencies"] = build_dependencies
        return result
