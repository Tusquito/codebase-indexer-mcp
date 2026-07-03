"""Two-hop client retrieval evaluation for multi_hop golden queries (ADR 0009).

Runs hop 1 (``query_text``) and hop 2 (``hop2_query_text``) via ``run_search``,
fuses with client-side RRF, and reports ranx metrics side-by-side vs single-pass.

Requires optional ``benchmark`` extra: ``uv sync --extra benchmark``.

Usage:
    python -m benchmarks.eval_multihop --output eval-multihop.json
    python -m benchmarks.eval_multihop --compare fixtures/eval_baseline.json
"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from codebase_indexer.indexer.backends.factory import create_backends, create_colbert_backend  # noqa: E402
from codebase_indexer.indexer.embedder import Embedder  # noqa: E402
from codebase_indexer.storage.qdrant import QdrantStorage  # noqa: E402
from codebase_indexer.tools.search_common import run_search  # noqa: E402

from benchmarks._connectivity import ollama_reachable, qdrant_reachable  # noqa: E402
from benchmarks._settings import load_settings  # noqa: E402
from benchmarks.eval_retrieval import (  # noqa: E402
    DEFAULT_GOLDEN,
    DEFAULT_METRICS,
    GoldenEntry,
    build_run_dict,
    load_golden,
    resolve_labels,
)
from benchmarks.multihop_rrf import fuse_hop_rrf  # noqa: E402


def filter_multihop_entries(entries: list[GoldenEntry]) -> list[GoldenEntry]:
    """Return ``multi_hop`` tagged entries that define ``hop2_query_text``."""
    multi = [e for e in entries if "multi_hop" in e.tags]
    missing = [e.query_id for e in multi if not e.hop2_query_text]
    if missing:
        raise ValueError(
            f"multi_hop entries missing hop2_query_text: {', '.join(missing)}"
        )
    return multi


async def run_multihop_evaluation(
    *,
    qdrant_url: str,
    ollama_url: str | None,
    golden_path: Path,
    hybrid_search: bool,
    rerank_enabled: bool,
    top_k: int,
    collection_override: str | None,
) -> dict[str, Any]:
    overrides: dict[str, object] = {
        "qdrant_url": qdrant_url,
        "hybrid_search": hybrid_search,
        "rerank_enabled": rerank_enabled,
        "release_models_after_index": False,
    }
    if ollama_url:
        overrides["ollama_url"] = ollama_url
    settings = load_settings(**overrides)
    entries = filter_multihop_entries(load_golden(golden_path))
    storage = QdrantStorage(settings)
    dense_backend, sparse_backend = create_backends(settings)
    colbert_backend = create_colbert_backend(settings) if settings.rerank_enabled else None
    embedder = Embedder(
        dense_backend=dense_backend,
        sparse_backend=sparse_backend,
        dense_embed_vector_size=settings.dense_embed_vector_size,
        hybrid=settings.hybrid_search,
        colbert_backend=colbert_backend,
        rerank=settings.rerank_enabled,
    )

    if settings.rerank_enabled:
        storage.reset_adaptive_stats()

    single_run: dict[str, dict[str, float]] = {}
    two_hop_run: dict[str, dict[str, float]] = {}
    qrels: dict[str, dict[str, int]] = {}
    per_query: list[dict[str, Any]] = []

    for entry in entries:
        collection = collection_override or entry.collection
        labels = resolve_labels(entry)
        qrels[entry.query_id] = labels

        hop1 = await run_search(
            storage=storage,
            embedder=embedder,
            query=entry.query_text,
            target_collections=[collection],
            top_k=top_k,
            language=None,
            min_score=0.0,
        )
        hop2 = await run_search(
            storage=storage,
            embedder=embedder,
            query=entry.hop2_query_text or "",
            target_collections=[collection],
            top_k=top_k,
            language=None,
            min_score=0.0,
        )
        fused = fuse_hop_rrf(
            [hop1, hop2],
            rrf_k=settings.rrf_k,
            top_k=top_k,
        )

        single_run[entry.query_id] = build_run_dict(hop1, top_k=top_k)
        two_hop_run[entry.query_id] = build_run_dict(fused, top_k=top_k)

        hit_single = {r.chunk_id for r in hop1[:top_k]} & set(labels.keys())
        hit_two = {r.chunk_id for r in fused[:top_k]} & set(labels.keys())
        per_query.append(
            {
                "query_id": entry.query_id,
                "collection": collection,
                "hop2_query_text": entry.hop2_query_text,
                "hits_single_pass": len(hit_single),
                "hits_two_hop": len(hit_two),
                "labels": len(labels),
            }
        )

    try:
        from ranx import evaluate
    except ImportError as exc:
        raise SystemExit(
            "ranx is required for eval_multihop. Install with: uv sync --extra benchmark"
        ) from exc

    def _metrics(run: dict[str, dict[str, float]]) -> dict[str, float]:
        raw = evaluate(qrels, run, DEFAULT_METRICS)
        if isinstance(raw, dict):
            return {name: round(float(raw[name]), 6) for name in DEFAULT_METRICS}
        return {DEFAULT_METRICS[0]: round(float(raw), 6)}

    return {
        "schema": 1,
        "params": {
            "golden": str(golden_path),
            "hybrid_search": hybrid_search,
            "rerank_enabled": rerank_enabled,
            "top_k": top_k,
            "rrf_k": settings.rrf_k,
            "dense_embed_model": settings.dense_embed_model,
            "sparse_embed_model": settings.sparse_embed_model,
            "qdrant_url": qdrant_url,
        },
        "metrics_single_pass": _metrics(single_run),
        "metrics_two_hop": _metrics(two_hop_run),
        "per_query": per_query,
        "n_queries": len(entries),
    }


def render_table(result: dict[str, Any]) -> str:
    params = result["params"]
    lines = [
        "=" * 64,
        f"Multi-hop eval  queries={result['n_queries']}  "
        f"hybrid={params['hybrid_search']}  rerank={params.get('rerank_enabled', False)}  "
        f"top_k={params['top_k']}  rrf_k={params['rrf_k']}",
        "-" * 64,
        "  Single-pass (hop 1 only):",
    ]
    for name, value in result["metrics_single_pass"].items():
        lines.append(f"    {name:<18}{value:>12.4f}")
    lines.append("-" * 64)
    lines.append("  Two-hop RRF fused:")
    for name, value in result["metrics_two_hop"].items():
        lines.append(f"    {name:<18}{value:>12.4f}")
    lines.append("=" * 64)
    return "\n".join(lines)


def compare_vs_baseline(
    result: dict[str, Any],
    baseline: dict[str, Any],
) -> str:
    """Compare two-hop metrics vs baseline ``multi_hop`` single-pass slice."""
    base = baseline.get("metrics_by_tag", {}).get("multi_hop", baseline.get("metrics", {}))
    cur = result["metrics_two_hop"]
    lines = ["", "Two-hop vs baseline multi_hop single-pass:", "-" * 72]
    for name in DEFAULT_METRICS:
        b = base.get(name, 0.0)
        c = cur.get(name, 0.0)
        if b == 0:
            pct = 0.0 if c == 0 else 100.0
        else:
            pct = (c - b) / b * 100.0
        lines.append(f"  {name:<34}{b:>10.4f} -> {c:>10.4f}  ({pct:+6.1f}%)")
    lines.append("-" * 72)
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Two-hop multi_hop golden-set evaluation (ranx)"
    )
    parser.add_argument(
        "--golden",
        type=Path,
        default=Path(os.environ.get("EVAL_GOLDEN", DEFAULT_GOLDEN)),
    )
    parser.add_argument(
        "--qdrant-url",
        default=os.environ.get("QDRANT_URL", "http://localhost:6333"),
    )
    parser.add_argument(
        "--ollama-url",
        default=os.environ.get("OLLAMA_URL", "http://localhost:11434"),
    )
    parser.add_argument("--collection", help="Override collection for all queries.")
    parser.add_argument("--top-k", type=int, default=int(os.environ.get("EVAL_TOP_K", "10")))
    parser.add_argument(
        "--no-hybrid",
        action="store_true",
        help="Disable hybrid search (dense-only A/B).",
    )
    parser.add_argument(
        "--rerank",
        action="store_true",
        help="Enable ColBERT reranking (requires indexed colbert multivectors).",
    )
    parser.add_argument("--output", help="Write results JSON to this path.")
    parser.add_argument(
        "--compare",
        help="Baseline JSON (uses metrics_by_tag.multi_hop for comparison).",
    )
    args = parser.parse_args()

    if not qdrant_reachable(args.qdrant_url):
        print(f"SKIP: Qdrant not reachable at {args.qdrant_url}", file=sys.stderr)
        return 0

    if not ollama_reachable(args.ollama_url):
        print(f"SKIP: Ollama not reachable at {args.ollama_url}", file=sys.stderr)
        return 0

    result = asyncio.run(
        run_multihop_evaluation(
            qdrant_url=args.qdrant_url,
            ollama_url=args.ollama_url,
            golden_path=args.golden,
            hybrid_search=not args.no_hybrid,
            rerank_enabled=args.rerank,
            top_k=args.top_k,
            collection_override=args.collection,
        )
    )

    print(render_table(result))

    if args.output:
        Path(args.output).write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(f"\nWrote {args.output}")

    if args.compare:
        baseline = json.loads(Path(args.compare).read_text(encoding="utf-8"))
        print(compare_vs_baseline(result, baseline))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
