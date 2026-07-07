#!/usr/bin/env python3
"""Smoke test for recommend_code against live Qdrant + TEI.

Run from mcp_server/ after stack is up:

    python scripts/smoke_recommend_code.py

Optional env: COLLECTION (default codebase-indexer-mcp).
Exits 0 on success, 1 on failure. Skips with message if Qdrant/TEI unreachable.
"""

from __future__ import annotations

import asyncio
import os
import sys
from pathlib import Path
from unittest.mock import MagicMock

# Allow imports when invoked as `python scripts/smoke_recommend_code.py`
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks._connectivity import qdrant_reachable, tei_reachable  # noqa: E402
from benchmarks._settings import load_settings  # noqa: E402
from codebase_indexer.context import AppContext  # noqa: E402
from codebase_indexer.tools.recommend import register_recommend_tool  # noqa: E402

COLLECTION = os.environ.get("COLLECTION", "codebase-indexer-mcp")
PATH_GLOB = f"{COLLECTION}/mcp_server/**/*.py"


def _register_handler(ctx: AppContext):
    mcp = MagicMock()
    captured: dict = {}

    def fake_tool(**kwargs):
        def decorator(fn):
            captured["handler"] = fn
            return fn

        return decorator

    mcp.tool = fake_tool
    register_recommend_tool(mcp, ctx)
    return captured["handler"]


async def main() -> int:
    settings = load_settings(preload_models=False, rerank_enabled=False)

    tei_url = settings.tei_url
    if not tei_reachable(tei_url):
        for alt in ("http://localhost:8080", "http://host.docker.internal:8080"):
            if alt != tei_url and tei_reachable(alt):
                settings = load_settings(
                    preload_models=False,
                    rerank_enabled=False,
                    tei_url=alt,
                )
                tei_url = alt
                break

    if not qdrant_reachable(settings.qdrant_url):
        qdrant_url = settings.qdrant_url
        for alt in ("http://localhost:6333", "http://host.docker.internal:6333"):
            if alt != qdrant_url and qdrant_reachable(alt):
                settings = load_settings(
                    preload_models=False,
                    rerank_enabled=False,
                    tei_url=tei_url,
                    qdrant_url=alt,
                )
                break
        if not qdrant_reachable(settings.qdrant_url):
            print(f"SKIP: Qdrant not reachable at {settings.qdrant_url}")
            return 0

    if not tei_reachable(settings.tei_url):
        print(f"SKIP: TEI not reachable at {settings.tei_url}")
        return 0

    ctx = AppContext.create(settings)
    handler = _register_handler(ctx)

    print(f"=== recommend_code smoke ({COLLECTION}) ===")

    r1 = await handler(
        collection=COLLECTION,
        positive_query="Qdrant recommendation API storage recommend method",
        negative_query="n8n workflow automation",
        limit=5,
        path_glob=PATH_GLOB,
        max_content_chars=200,
    )
    if not r1["results"]:
        print("FAIL: query-based recommend returned no results")
        return 1
    print(f"query-based: {len(r1['results'])} results")
    top = r1["results"][0]
    print(f"  top: score={top['score']:.4f} {top['rel_path']}:{top['start_line']}")

    pos_id = r1["results"][0]["chunk_id"]
    r2 = await handler(
        collection=COLLECTION,
        positive_chunk_ids=[pos_id],
        limit=3,
        path_glob=f"{COLLECTION}/mcp_server/src/**/*.py",
        max_content_chars=150,
    )
    if not r2["results"]:
        print("FAIL: chunk-id recommend returned no results")
        return 1
    print(f"chunk-id: {len(r2['results'])} results")

    try:
        await handler(
            collection=COLLECTION,
            positive_chunk_ids=["nonexistent.py:999"],
        )
        print("FAIL: expected ValueError for unknown chunk_id")
        return 1
    except ValueError:
        print("fail-fast: OK")

    print("=== All smoke tests passed ===")
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
