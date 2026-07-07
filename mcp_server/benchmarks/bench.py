"""Async benchmark runner for the codebase indexer.

Measures the metrics the enhancement plan targets:
  - indexing throughput (full + incremental, chunks/sec, peak RSS)
  - filtered-lookup latency p50/p95 (the payload-index win)
  - search latency p50/p95 (hybrid + language-filtered)

Standalone — no new runtime dependencies (time.perf_counter + statistics).
Skips cleanly when Qdrant is unreachable, mirroring the storage integration
test, so it is safe to invoke from CI or a dev box without Qdrant.

Usage:
    python -m benchmarks.bench --files 400 --output results.json
    python -m benchmarks.bench --no-payload-indexes --output baseline.json
    python -m benchmarks.bench --compare baseline.json --threshold 10

Toggling payload indexes (``--no-payload-indexes`` vs default) against the same
corpus is how the harness proves the Tier 2 lookup-latency improvement.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import math
import os
import shutil
import sys
import tempfile
import time
from pathlib import Path
from typing import Any, Awaitable, Callable

# Allow ``python benchmarks/bench.py`` as well as ``-m benchmarks.bench``.
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from benchmarks._connectivity import tei_reachable, qdrant_reachable  # noqa: E402
from benchmarks._settings import load_settings  # noqa: E402
from codebase_indexer.indexer.backends.factory import (
    create_backends,
    create_colbert_backend,
)
from codebase_indexer.indexer.embedder import Embedder  # noqa: E402
from codebase_indexer.indexer.pipeline import run_pipeline  # noqa: E402
from codebase_indexer.memory import get_rss_mb  # noqa: E402
from codebase_indexer.storage.qdrant import QdrantStorage  # noqa: E402

from benchmarks.corpus import generate_corpus  # noqa: E402


def _pct(values: list[float], p: float) -> float:
    if not values:
        return 0.0
    s = sorted(values)
    if len(s) == 1:
        return s[0]
    k = (len(s) - 1) * (p / 100.0)
    lo = math.floor(k)
    hi = math.ceil(k)
    if lo == hi:
        return s[int(k)]
    return s[lo] + (s[hi] - s[lo]) * (k - lo)


async def _time_loop(
    fn: Callable[[int], Awaitable[Any]], iterations: int
) -> dict[str, float]:
    """Run ``fn(i)`` ``iterations`` times, returning latency stats in ms."""
    samples: list[float] = []
    for i in range(iterations):
        t0 = time.perf_counter()
        await fn(i)
        samples.append((time.perf_counter() - t0) * 1000.0)
    return {
        "p50": round(_pct(samples, 50), 3),
        "p95": round(_pct(samples, 95), 3),
        "mean": round(sum(samples) / len(samples), 3),
        "n": iterations,
    }


async def _collect_samples(storage: QdrantStorage, collection: str) -> dict[str, list]:
    """Scroll a slice of the collection to gather realistic lookup keys."""
    client = await storage._get_client()
    chunk_ids: list[str] = []
    rel_paths: list[str] = []
    symbol_names: list[str] = []
    languages: set[str] = set()

    offset = None
    while True:
        points, offset = await client.scroll(
            collection_name=collection,
            limit=5000,
            offset=offset,
            with_payload=["chunk_id", "rel_path", "symbol_name", "language"],
            with_vectors=False,
        )
        for p in points:
            payload = p.payload or {}
            if payload.get("chunk_id"):
                chunk_ids.append(payload["chunk_id"])
            if payload.get("rel_path"):
                rel_paths.append(payload["rel_path"])
            if payload.get("symbol_name"):
                symbol_names.append(payload["symbol_name"])
            if payload.get("language"):
                languages.add(payload["language"])
        if offset is None:
            break

    return {
        "chunk_ids": list(dict.fromkeys(chunk_ids)),
        "rel_paths": list(dict.fromkeys(rel_paths)),
        "symbol_names": list(dict.fromkeys(symbol_names)),
        "languages": sorted(languages),
    }


async def run_benchmark(
    *,
    qdrant_url: str,
    files: int,
    seed: int,
    iterations: int,
    collection: str,
    payload_indexes: bool,
    rerank_enabled: bool,
    keep: bool,
    colbert_url: str | None = None,
    colbert_embed_backend: str | None = None,
    colbert_sidecar_health: dict[str, object] | None = None,
) -> dict[str, Any]:
    settings_overrides: dict[str, object] = {
        "qdrant_url": qdrant_url,
        "payload_indexes": payload_indexes,
        "hybrid_search": True,
        "rerank_enabled": rerank_enabled,
        # Keep models resident across the run (we index then search).
        "release_models_after_index": False,
    }
    if colbert_embed_backend is not None:
        settings_overrides["colbert_embed_backend"] = colbert_embed_backend
    if colbert_url is not None:
        settings_overrides["colbert_url"] = colbert_url

    settings = load_settings(**settings_overrides)

    tmp = Path(tempfile.mkdtemp(prefix="bench_corpus_"))
    sub_path = "/" + collection
    try:
        corpus = generate_corpus(tmp, n_files=files, seed=seed, project_name=collection)
        settings = settings.model_copy(update={"workspace_path": str(tmp)})

        storage = QdrantStorage(settings)
        client = await storage._get_client()

        # Start from a clean collection so timings are not skewed by prior data.
        try:
            await client.delete_collection(collection)
        except Exception:
            pass

        # --- Indexing: full ---
        t0 = time.perf_counter()
        full = await run_pipeline(
            settings=settings, storage=storage, collection=collection,
            sub_path=sub_path, force=True,
        )
        full_index_s = time.perf_counter() - t0

        # --- Indexing: incremental (everything unchanged → mostly skipped) ---
        t0 = time.perf_counter()
        incr = await run_pipeline(
            settings=settings, storage=storage, collection=collection,
            sub_path=sub_path, force=False,
        )
        incremental_s = time.perf_counter() - t0

        # --- Gather realistic lookup keys ---
        samples = await _collect_samples(storage, collection)
        chunk_ids = samples["chunk_ids"] or [""]
        rel_paths = samples["rel_paths"] or [""]
        symbol_names = samples["symbol_names"] or [""]
        languages = samples["languages"] or ["python"]

        # --- Pre-embed a query once so search timings isolate storage cost ---
        dense_backend, sparse_backend = create_backends(settings)
        colbert_backend = (
            create_colbert_backend(settings) if settings.rerank_enabled else None
        )
        embedder = Embedder(
            dense_backend=dense_backend,
            sparse_backend=sparse_backend,
            dense_embed_vector_size=settings.dense_embed_vector_size,
            hybrid=settings.hybrid_search,
            colbert_backend=colbert_backend,
            rerank=settings.rerank_enabled,
        )
        dense_vec, sparse_vec, colbert_vec = await embedder.embed_query(
            "service handler request processing"
        )

        lookups: dict[str, dict] = {}

        async def _get_chunk(i: int) -> None:
            await storage.get_chunk_by_id(collection, chunk_ids[i % len(chunk_ids)])

        async def _file_symbols(i: int) -> None:
            await storage.scroll_file_symbols(collection, rel_paths[i % len(rel_paths)])

        async def _find_symbol(i: int) -> None:
            await storage.find_symbol_in_collections(
                symbol_names[i % len(symbol_names)], [collection], limit_per_collection=10
            )

        async def _search_hybrid(i: int) -> None:
            await storage.search(
                collection=collection, dense_vector=dense_vec,
                sparse_vector=sparse_vec, top_k=10,
            )

        async def _search_rerank(i: int) -> None:
            await storage.search(
                collection=collection,
                dense_vector=dense_vec,
                sparse_vector=sparse_vec,
                colbert_vector=colbert_vec,
                top_k=10,
            )

        async def _search_lang(i: int) -> None:
            await storage.search(
                collection=collection, dense_vector=dense_vec,
                sparse_vector=sparse_vec, top_k=10,
                language=languages[i % len(languages)],
            )

        lookups["get_chunk_by_id"] = await _time_loop(_get_chunk, iterations)
        lookups["scroll_file_symbols"] = await _time_loop(_file_symbols, iterations)
        lookups["find_symbol_in_collections"] = await _time_loop(_find_symbol, iterations)
        lookups["search_hybrid"] = await _time_loop(_search_hybrid, iterations)
        if settings.rerank_enabled and colbert_vec is not None:
            storage.reset_adaptive_stats()
            lookups["search_hybrid_rerank"] = await _time_loop(_search_rerank, iterations)
        lookups["search_language_filtered"] = await _time_loop(_search_lang, iterations)

        # --- delete_by_paths: single timed batch delete (mutating, so last) ---
        del_paths = rel_paths[: min(50, len(rel_paths))]
        t0 = time.perf_counter()
        await storage.delete_by_paths(collection, del_paths)
        delete_ms = round((time.perf_counter() - t0) * 1000.0, 3)

        total_chunks = full.total_chunks or 1
        params: dict[str, Any] = {
            "files": files,
            "seed": seed,
            "iterations": iterations,
            "payload_indexes": payload_indexes,
            "hybrid_search": settings.hybrid_search,
            "rerank_enabled": settings.rerank_enabled,
            "dense_embed_model": settings.dense_embed_model,
        }
        if settings.rerank_enabled:
            params["colbert_embed_backend"] = settings.colbert_embed_backend
            params["rerank_adaptive_enabled"] = settings.rerank_adaptive_enabled
            params["rerank_adaptive_gap"] = settings.rerank_adaptive_gap
            if settings.colbert_embed_backend == "remote":
                params["colbert_url"] = settings.colbert_url
                if colbert_sidecar_health is not None:
                    params["colbert_sidecar_device"] = colbert_sidecar_health.get("device")
                    params["colbert_sidecar_cuda_available"] = colbert_sidecar_health.get(
                        "cuda_available"
                    )
        result = {
            "schema": 1,
            "params": params,
            "corpus": {
                "n_files": corpus.n_files,
                "files_by_ext": corpus.files_by_ext,
                "total_bytes": corpus.total_bytes,
            },
            "indexing": {
                "full_index_s": round(full_index_s, 3),
                "incremental_s": round(incremental_s, 3),
                "total_chunks": full.total_chunks,
                "indexed_files": full.indexed_files,
                "chunks_per_s": round(total_chunks / full_index_s, 2) if full_index_s else 0.0,
                "incremental_skipped": incr.skipped_files,
                "peak_rss_mb": get_rss_mb(),
            },
            "lookups_ms": lookups,
            "delete_by_paths_ms": {"batch_size": len(del_paths), "elapsed_ms": delete_ms},
        }
        if settings.rerank_enabled and colbert_vec is not None:
            result["adaptive_rerank"] = storage.adaptive_rerank_stats.as_dict()

        if not keep:
            try:
                await client.delete_collection(collection)
            except Exception:
                pass
        return result
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


# ---------------------------------------------------------------------------
# Reporting + comparison
# ---------------------------------------------------------------------------

def render_table(result: dict[str, Any]) -> str:
    idx = result["indexing"]
    lines = [
        "=" * 64,
        f"Benchmark  files={result['params']['files']}  "
        f"payload_indexes={result['params']['payload_indexes']}  "
        f"rerank={result['params'].get('rerank_enabled', False)}  "
        f"iters={result['params']['iterations']}",
        "-" * 64,
        f"  full index        : {idx['full_index_s']:>8.3f} s  "
        f"({idx['chunks_per_s']} chunks/s, {idx['total_chunks']} chunks)",
        f"  incremental index : {idx['incremental_s']:>8.3f} s  "
        f"(skipped {idx['incremental_skipped']} files)",
        f"  peak RSS          : {idx['peak_rss_mb']:>8} MB",
        "-" * 64,
        f"  {'lookup':<30}{'p50 ms':>10}{'p95 ms':>10}",
    ]
    for name, s in result["lookups_ms"].items():
        lines.append(f"  {name:<30}{s['p50']:>10.3f}{s['p95']:>10.3f}")
    adaptive = result.get("adaptive_rerank")
    if adaptive is not None:
        lines.append(
            f"  {'adaptive_rerank skip_rate':<30}"
            f"{adaptive['skip_rate'] * 100:>9.1f}%"
            f"  ({adaptive['skipped']}/{adaptive['total']} skipped)"
        )
    d = result["delete_by_paths_ms"]
    lines.append(f"  {'delete_by_paths (batch=' + str(d['batch_size']) + ')':<30}"
                 f"{d['elapsed_ms']:>10.3f}{'':>10}")
    lines.append("=" * 64)
    return "\n".join(lines)


def compare(current: dict[str, Any], baseline: dict[str, Any], threshold_pct: float) -> tuple[str, bool]:
    """Return (report, regressed). A metric regresses when it gets worse by > threshold.

    Latency metrics: higher = worse. Throughput (chunks_per_s): lower = worse.
    """
    lines = ["", "Comparison vs baseline (negative = improvement for latency):", "-" * 72]
    regressed = False

    def _delta(name: str, cur: float, base: float, lower_is_better: bool) -> None:
        nonlocal regressed
        if base == 0:
            pct = 0.0
        else:
            pct = (cur - base) / base * 100.0
        worse = pct > threshold_pct if lower_is_better else pct < -threshold_pct
        flag = "  REGRESSION" if worse else ""
        if worse:
            regressed = True
        lines.append(f"  {name:<34}{base:>10.3f} -> {cur:>10.3f}  ({pct:+6.1f}%){flag}")

    ci, bi = current["indexing"], baseline["indexing"]
    _delta("indexing.chunks_per_s", ci["chunks_per_s"], bi["chunks_per_s"], lower_is_better=False)
    _delta("indexing.full_index_s", ci["full_index_s"], bi["full_index_s"], lower_is_better=True)

    for name, cur in current["lookups_ms"].items():
        base = baseline["lookups_ms"].get(name)
        if base:
            _delta(f"lookup.{name}.p95", cur["p95"], base["p95"], lower_is_better=True)

    cur_del = current["delete_by_paths_ms"]["elapsed_ms"]
    base_del = baseline["delete_by_paths_ms"]["elapsed_ms"]
    _delta("delete_by_paths.elapsed_ms", cur_del, base_del, lower_is_better=True)

    lines.append("-" * 72)
    return "\n".join(lines), regressed


def main() -> int:
    parser = argparse.ArgumentParser(description="Codebase indexer benchmark runner")
    parser.add_argument("--qdrant-url", default=os.environ.get("QDRANT_URL", "http://localhost:6333"))
    parser.add_argument("--files", type=int, default=int(os.environ.get("BENCH_FILES", "300")))
    parser.add_argument("--seed", type=int, default=int(os.environ.get("BENCH_SEED", "1234")))
    parser.add_argument("--iterations", type=int, default=int(os.environ.get("BENCH_ITERS", "50")))
    parser.add_argument("--collection", default=os.environ.get("BENCH_COLLECTION", "benchproj"))
    parser.add_argument("--no-payload-indexes", action="store_true",
                        help="Disable Qdrant payload indexes (baseline mode).")
    parser.add_argument(
        "--rerank",
        action="store_true",
        help="Enable ColBERT reranking during index + search latency probes.",
    )
    parser.add_argument("--keep", action="store_true", help="Keep the benchmark collection afterwards.")
    parser.add_argument("--output", help="Write results JSON to this path.")
    parser.add_argument("--compare", help="Baseline JSON to compare the current run against.")
    parser.add_argument("--threshold", type=float, default=float(os.environ.get("BENCH_THRESHOLD", "0")),
                        help="Fail (exit 1) if a metric regresses by more than this percent. 0 disables.")
    args = parser.parse_args()

    if not qdrant_reachable(args.qdrant_url):
        print(f"SKIP: Qdrant not reachable at {args.qdrant_url}", file=sys.stderr)
        if args.output:
            Path(args.output).write_text(
                json.dumps({"skipped": True, "reason": "qdrant_unreachable"}),
                encoding="utf-8",
            )
        return 0

    tei_url = os.environ.get("TEI_URL", "http://localhost:8080")
    if not tei_reachable(tei_url):
        print(f"SKIP: TEI not reachable at {tei_url}", file=sys.stderr)
        if args.output:
            Path(args.output).write_text(
                json.dumps({"skipped": True, "reason": "tei_unreachable"}),
                encoding="utf-8",
            )
        return 0

    result = asyncio.run(run_benchmark(
        qdrant_url=args.qdrant_url,
        files=args.files,
        seed=args.seed,
        iterations=args.iterations,
        collection=args.collection,
        payload_indexes=not args.no_payload_indexes,
        rerank_enabled=args.rerank,
        keep=args.keep,
    ))

    print(render_table(result))

    if args.output:
        Path(args.output).write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(f"\nWrote {args.output}")

    if args.compare:
        baseline = json.loads(Path(args.compare).read_text(encoding="utf-8"))
        report, regressed = compare(result, baseline, args.threshold or 1e9)
        print(report)
        if args.threshold and regressed:
            print("FAIL: one or more metrics regressed beyond threshold", file=sys.stderr)
            return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
