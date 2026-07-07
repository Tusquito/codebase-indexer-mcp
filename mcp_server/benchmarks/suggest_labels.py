"""Suggest golden-set label candidates from live search results.

Prints top search hits as ``rel_path:start_line`` aliases (collection prefix
stripped) for maintainer copy into ``fixtures/golden_queries.jsonl``.

Usage:
    python -m benchmarks.suggest_labels "class Embedder embedder.py"
    python -m benchmarks.suggest_labels "run_pipeline indexing" --collection codebase-indexer-mcp --top-k 8
"""

from __future__ import annotations

import argparse
import asyncio
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks._connectivity import tei_reachable, qdrant_reachable  # noqa: E402
from benchmarks.eval_retrieval import _settings  # noqa: E402
from codebase_indexer.indexer.backends.factory import create_backends  # noqa: E402
from codebase_indexer.indexer.embedder import Embedder  # noqa: E402
from codebase_indexer.storage.qdrant import QdrantStorage  # noqa: E402
from codebase_indexer.tools.search_common import run_search  # noqa: E402


def _strip_collection_prefix(rel_path: str, collection: str) -> str:
    prefix = f"{collection}/"
    if rel_path.startswith(prefix):
        return rel_path[len(prefix) :]
    return rel_path


async def suggest(
    *,
    query: str,
    collection: str,
    top_k: int,
    qdrant_url: str,
    tei_url: str,
) -> None:
    settings = _settings(
        qdrant_url=qdrant_url,
        tei_url=tei_url,
        hybrid_search=True,
        release_models_after_index=False,
    )
    storage = QdrantStorage(settings)
    dense, sparse = create_backends(settings)
    embedder = Embedder(
        dense_backend=dense,
        sparse_backend=sparse,
        dense_embed_vector_size=settings.dense_embed_vector_size,
        hybrid=True,
    )
    results = await run_search(
        storage=storage,
        embedder=embedder,
        query=query,
        target_collections=[collection],
        top_k=top_k,
        language=None,
        min_score=0.0,
    )

    print(f"Query: {query!r}")
    print(f"Collection: {collection}  top_k={top_k}\n")
    print(f"{'rank':<5}{'alias':<58}{'symbol':<28}{'score'}")
    print("-" * 100)
    for rank, r in enumerate(results, start=1):
        alias = f"{_strip_collection_prefix(r.rel_path, collection)}:{r.start_line}"
        sym = r.symbol_name or "-"
        print(f"{rank:<5}{alias:<58}{sym:<28}{r.score:.4f}")
    print("\nSuggested JSON alias entries (grade 2 = primary, 1 = related):")
    for rank, r in enumerate(results[: min(3, len(results))], start=1):
        alias = f"{_strip_collection_prefix(r.rel_path, collection)}:{r.start_line}"
        grade = 2 if rank == 1 else 1
        print(f'  "{alias}": {grade},')


def main() -> int:
    parser = argparse.ArgumentParser(description="Suggest golden-set label aliases from search")
    parser.add_argument("query", help="Search query text")
    parser.add_argument(
        "--collection",
        default=os.environ.get("EVAL_COLLECTION", "codebase-indexer-mcp"),
    )
    parser.add_argument("--top-k", type=int, default=8)
    parser.add_argument(
        "--qdrant-url",
        default=os.environ.get("QDRANT_URL", "http://localhost:6333"),
    )
    parser.add_argument(
        "--tei-url",
        default=os.environ.get("TEI_URL", "http://localhost:8080"),
    )
    args = parser.parse_args()

    if not qdrant_reachable(args.qdrant_url):
        print(f"SKIP: Qdrant not reachable at {args.qdrant_url}", file=sys.stderr)
        return 0
    if not tei_reachable(args.tei_url):
        print(f"SKIP: TEI not reachable at {args.tei_url}", file=sys.stderr)
        return 0

    asyncio.run(
        suggest(
            query=args.query,
            collection=args.collection,
            top_k=args.top_k,
            qdrant_url=args.qdrant_url,
            tei_url=args.tei_url,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
