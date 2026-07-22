"""Offline dense retrieval eval for a HuggingFace fine-tuned checkpoint (ADR 0020).

Re-embeds the indexed corpus with a sentence-transformers checkpoint and scores
the golden set with ranx — required because fine-tuned vectors live in a
different space than the Qdrant index built with base Ollama embeddings.

Hybrid BM25 is **not** fused here; compare primarily to Jina **overall** metrics
and note dense-only vs hybrid caveat, or run ``--model base`` on the same corpus
for apples-to-apples dense comparison.

Usage:
    python -m benchmarks.train.eval_finetuned_checkpoint \\
        --checkpoint benchmarks/train/outputs/checkpoints/best \\
        --compare fixtures/eval_baseline_jina.json
"""

from __future__ import annotations

import argparse
import asyncio
import json
import sys
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src"))

from benchmarks.eval_retrieval import (  # noqa: E402
    DEFAULT_GOLDEN,
    DEFAULT_METRICS,
    GoldenEntry,
    compute_tag_metrics,
    load_golden,
    render_table,
    resolve_labels,
)
from benchmarks.eval_retrieval import compare as compare_baselines  # noqa: E402
from benchmarks._settings import load_settings  # noqa: E402
from codebase_indexer.storage.qdrant import QdrantStorage  # noqa: E402

DEFAULT_COLLECTION = "codebase-indexer-mcp"
DEFAULT_BASE_MODEL = "Qwen/Qwen3-Embedding-4B"


async def scroll_corpus(
    storage: QdrantStorage,
    collection: str,
) -> list[tuple[str, str]]:
    """Return (chunk_id, content) for all points with non-empty content."""
    client = await storage._get_client()
    rows: list[tuple[str, str]] = []
    offset = None
    while True:
        points, next_offset = await client.scroll(
            collection_name=collection,
            limit=256,
            offset=offset,
            with_payload=["chunk_id", "content"],
            with_vectors=False,
        )
        for point in points:
            payload = point.payload or {}
            chunk_id = payload.get("chunk_id") or str(point.id)
            content = (payload.get("content") or "").strip()
            if content:
                rows.append((chunk_id, content))
        if next_offset is None:
            break
        offset = next_offset
    return rows


def embed_corpus(model: Any, texts: list[str], *, batch_size: int) -> list[list[float]]:
    embeddings = model.encode(
        texts,
        batch_size=batch_size,
        convert_to_numpy=True,
        normalize_embeddings=True,
        show_progress_bar=True,
    )
    return [row.tolist() for row in embeddings]


def dense_search(
    query_vec: list[float],
    corpus_ids: list[str],
    corpus_vecs: list[list[float]],
    *,
    top_k: int,
) -> list[tuple[str, float]]:
    scores: list[tuple[str, float]] = []
    for chunk_id, vec in zip(corpus_ids, corpus_vecs, strict=True):
        score = sum(a * b for a, b in zip(query_vec, vec, strict=True))
        scores.append((chunk_id, score))
    scores.sort(key=lambda x: x[1], reverse=True)
    return scores[:top_k]


async def run_offline_eval(
    *,
    model: Any,
    entries: list[GoldenEntry],
    storage: QdrantStorage,
    collection: str,
    top_k: int,
    embed_batch_size: int,
) -> dict[str, Any]:
    corpus = await scroll_corpus(storage, collection)
    if not corpus:
        raise SystemExit(f"No corpus chunks in collection {collection!r}")

    corpus_ids = [cid for cid, _ in corpus]
    corpus_texts = [text for _, text in corpus]
    corpus_vecs = embed_corpus(model, corpus_texts, batch_size=embed_batch_size)

    qrels: dict[str, dict[str, int]] = {}
    run: dict[str, dict[str, float]] = {}
    per_query: list[dict[str, Any]] = []

    for entry in entries:
        coll = entry.collection or collection
        if coll != collection:
            continue
        labels = resolve_labels(entry)
        qrels[entry.query_id] = labels

        query_vec = embed_corpus(model, [entry.query_text], batch_size=1)[0]
        hits = dense_search(query_vec, corpus_ids, corpus_vecs, top_k=top_k)
        run[entry.query_id] = {cid: score for cid, score in hits}

        hit_ids = {cid for cid, _ in hits}
        relevant = set(labels.keys())
        per_query.append(
            {
                "query_id": entry.query_id,
                "tags": entry.tags,
                "hits_in_top_k": len(hit_ids & relevant),
                "labels": len(relevant),
            }
        )

    from ranx import evaluate

    metrics_raw = evaluate(qrels, run, DEFAULT_METRICS)
    if isinstance(metrics_raw, dict):
        metrics = {name: round(float(metrics_raw[name]), 6) for name in DEFAULT_METRICS}
    else:
        metrics = {DEFAULT_METRICS[0]: round(float(metrics_raw), 6)}

    metrics_by_tag = compute_tag_metrics(entries, qrels, run)

    return {
        "schema": 1,
        "mode": "offline_dense_reembed",
        "params": {
            "golden": str(DEFAULT_GOLDEN),
            "collection": collection,
            "top_k": top_k,
            "n_corpus_chunks": len(corpus),
            "note": "Dense-only in-memory search; corpus re-embedded with checkpoint. Jina baseline used hybrid RRF.",
        },
        "metrics": metrics,
        "metrics_by_tag": metrics_by_tag,
        "n_queries": len(qrels),
        "per_query": per_query,
    }


def load_st_model(checkpoint: Path | None, base_model: str) -> Any:
    from sentence_transformers import SentenceTransformer

    path = str(checkpoint) if checkpoint is not None else base_model
    return SentenceTransformer(path)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Offline dense eval for fine-tuned HF checkpoint vs Jina baseline"
    )
    parser.add_argument(
        "--checkpoint",
        type=Path,
        default=Path(__file__).resolve().parent / "outputs" / "checkpoints" / "best",
        help="Path to saved SentenceTransformer checkpoint",
    )
    parser.add_argument(
        "--base-model",
        default=DEFAULT_BASE_MODEL,
        help="HuggingFace id when --checkpoint omitted",
    )
    parser.add_argument(
        "--golden",
        type=Path,
        default=DEFAULT_GOLDEN,
    )
    parser.add_argument(
        "--collection",
        default=DEFAULT_COLLECTION,
    )
    parser.add_argument(
        "--qdrant-url",
        default="http://localhost:6333",
    )
    parser.add_argument(
        "--top-k",
        type=int,
        default=10,
    )
    parser.add_argument(
        "--embed-batch-size",
        type=int,
        default=8,
    )
    parser.add_argument(
        "--compare",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "fixtures" / "eval_baseline_jina.json",
        help="Baseline JSON (default: eval_baseline_jina.json)",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Write results JSON",
    )
    args = parser.parse_args()

    if not args.checkpoint.is_dir() and args.checkpoint != Path("base"):
        print(f"ERROR: checkpoint not found: {args.checkpoint}", file=sys.stderr)
        return 1

    checkpoint = None if str(args.checkpoint) == "base" else args.checkpoint
    print(f"Loading model from {checkpoint or args.base_model}...", file=sys.stderr)
    model = load_st_model(checkpoint, args.base_model)

    settings = load_settings(qdrant_url=args.qdrant_url)
    storage = QdrantStorage(settings)
    entries = load_golden(args.golden)

    result = asyncio.run(
        run_offline_eval(
            model=model,
            entries=entries,
            storage=storage,
            collection=args.collection,
            top_k=args.top_k,
            embed_batch_size=args.embed_batch_size,
        )
    )

    print(render_table(result))

    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print(f"\nWrote {args.output}")

    if args.compare.is_file():
        baseline = json.loads(args.compare.read_text(encoding="utf-8"))
        report, regressed = compare_baselines(result, baseline, threshold_pct=0.0)
        print(report)
        print(
            "\nNote: checkpoint eval is dense-only re-embed; "
            "Jina baseline is hybrid RRF unless baseline params say otherwise.",
            file=sys.stderr,
        )
        if regressed:
            print("Fine-tuned dense recall@10 is below Jina hybrid baseline.", file=sys.stderr)
            return 2

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
