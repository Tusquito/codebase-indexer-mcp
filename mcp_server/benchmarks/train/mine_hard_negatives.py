"""Mine hard negatives from base Qwen3 hybrid search misses (ADR 0020).

For each training pair, runs the same ``run_search`` path as eval_retrieval
with **base** Qwen3 embeddings. Top-k results that are not labeled positives
become hard negatives appended to the pair.

Usage:
    python -m benchmarks.train.mine_hard_negatives --input pairs.jsonl
    python -m benchmarks.train.mine_hard_negatives --input pairs.jsonl --top-k 10
"""

from __future__ import annotations

import argparse
import asyncio
import copy
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src"))

from benchmarks.eval_retrieval import (  # noqa: E402
    DEFAULT_GOLDEN,
    GoldenEntry,
    load_golden,
    resolve_labels,
)
from benchmarks.train._schema import TrainingPair, read_jsonl, write_jsonl  # noqa: E402
from codebase_indexer.indexer.backends.factory import create_backends  # noqa: E402
from codebase_indexer.indexer.embedder import Embedder  # noqa: E402
from codebase_indexer.storage.qdrant import QdrantStorage  # noqa: E402
from codebase_indexer.tools.search_common import run_search  # noqa: E402

from benchmarks._settings import load_settings  # noqa: E402

DEFAULT_TOP_K = 10


def _golden_by_id(path: Path) -> dict[str, GoldenEntry]:
    return {e.query_id: e for e in load_golden(path)}


async def mine_negatives_for_pair(
    pair: TrainingPair,
    entry: GoldenEntry,
    *,
    storage: QdrantStorage,
    embedder: Embedder,
    top_k: int,
    collection: str,
) -> list[str]:
    """Return hard-negative passage texts from base-model search misses."""
    labels = resolve_labels(entry)
    label_ids = set(labels.keys())

    results = await run_search(
        storage=storage,
        embedder=embedder,
        query=pair.query,
        target_collections=[collection],
        top_k=top_k,
        language=None,
        min_score=0.0,
    )

    negatives: list[str] = []
    seen: set[str] = set()
    for result in results:
        if result.chunk_id in label_ids:
            continue
        text = (result.content or "").strip()
        if not text or text in seen:
            continue
        seen.add(text)
        negatives.append(text)
    return negatives


async def mine_hard_negatives(
    pairs: list[TrainingPair],
    golden_by_id: dict[str, GoldenEntry],
    *,
    storage: QdrantStorage,
    embedder: Embedder,
    top_k: int,
    collection_override: str | None,
) -> list[TrainingPair]:
    updated: list[TrainingPair] = []
    for pair in pairs:
        entry = golden_by_id.get(pair.query_id)
        if entry is None:
            print(
                f"WARN: no golden entry for {pair.query_id!r}; skipping mining",
                file=sys.stderr,
            )
            updated.append(pair)
            continue

        collection = collection_override or entry.collection
        negatives = await mine_negatives_for_pair(
            pair,
            entry,
            storage=storage,
            embedder=embedder,
            top_k=top_k,
            collection=collection,
        )
        new_pair = copy.copy(pair)
        new_pair.negatives = negatives
        updated.append(new_pair)
    return updated


async def run_mine(
    input_path: Path,
    output_path: Path,
    golden_path: Path,
    *,
    qdrant_url: str,
    ollama_url: str | None,
    top_k: int,
    collection_override: str | None,
    hybrid_search: bool,
) -> int:
    pairs = read_jsonl(input_path)
    if not pairs:
        print(f"ERROR: no pairs in {input_path}", file=sys.stderr)
        return 1

    golden_by_id = _golden_by_id(golden_path)
    overrides: dict[str, object] = {
        "qdrant_url": qdrant_url,
        "hybrid_search": hybrid_search,
        "rerank_enabled": False,
        "release_models_after_index": False,
    }
    if ollama_url:
        overrides["ollama_url"] = ollama_url
    settings = load_settings(**overrides)

    storage = QdrantStorage(settings)
    dense_backend, sparse_backend = create_backends(settings)
    embedder = Embedder(
        dense_backend=dense_backend,
        sparse_backend=sparse_backend,
        dense_embed_vector_size=settings.dense_embed_vector_size,
        hybrid=settings.hybrid_search,
        colbert_backend=None,
        rerank=False,
    )

    updated = await mine_hard_negatives(
        pairs,
        golden_by_id,
        storage=storage,
        embedder=embedder,
        top_k=top_k,
        collection_override=collection_override,
    )
    write_jsonl(updated, output_path)

    with_negs = sum(1 for p in updated if p.negatives)
    total_negs = sum(len(p.negatives) for p in updated)
    print(
        f"Wrote {len(updated)} pairs to {output_path} "
        f"({with_negs} with negatives, {total_negs} total negatives)",
        file=sys.stderr,
    )
    return 0


def main() -> None:
    default_out = Path(__file__).resolve().parent / "outputs" / "pairs_with_negatives.jsonl"
    parser = argparse.ArgumentParser(
        description="Mine hard negatives from base Qwen3 hybrid search"
    )
    parser.add_argument(
        "--input",
        type=Path,
        required=True,
        help="Input JSONL from export_golden_pairs",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=default_out,
        help="Output JSONL with negatives populated",
    )
    parser.add_argument(
        "--golden",
        type=Path,
        default=DEFAULT_GOLDEN,
        help="Path to golden_queries.jsonl (for label ids)",
    )
    parser.add_argument(
        "--qdrant-url",
        default="http://localhost:6333",
        help="Qdrant HTTP URL",
    )
    parser.add_argument(
        "--ollama-url",
        default=None,
        help="Ollama HTTP URL (default: from .env or http://localhost:11434)",
    )
    parser.add_argument(
        "--collection",
        default=None,
        help="Override collection name for search",
    )
    parser.add_argument(
        "--top-k",
        type=int,
        default=DEFAULT_TOP_K,
        help="Search depth for hard-negative mining",
    )
    parser.add_argument(
        "--no-hybrid",
        action="store_true",
        help="Use dense-only search (default: hybrid RRF)",
    )
    args = parser.parse_args()

    args.output.parent.mkdir(parents=True, exist_ok=True)
    raise SystemExit(
        asyncio.run(
            run_mine(
                args.input,
                args.output,
                args.golden,
                qdrant_url=args.qdrant_url,
                ollama_url=args.ollama_url,
                top_k=args.top_k,
                collection_override=args.collection,
                hybrid_search=not args.no_hybrid,
            )
        )
    )


if __name__ == "__main__":
    main()
