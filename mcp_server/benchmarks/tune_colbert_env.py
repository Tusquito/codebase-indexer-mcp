"""Sweep .env tuning knobs via docker exec and rank by throughput + stability."""

from __future__ import annotations

import json
import subprocess
import sys
import time
from itertools import product
from pathlib import Path

CONTAINER = "codeindexer_mcp"
PYTHON = "/app/.venv/bin/python"
BENCH = "benchmarks.bench_colbert_sidecar"
FILES = 50
SEED = 1234
OUT_DIR = Path("/tmp/tune_colbert")


def run_bench(env: dict[str, str]) -> dict | None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    tag = "_".join(f"{k}={v}" for k, v in sorted(env.items()))
    out_path = OUT_DIR / f"{tag}.json"
    cmd = ["docker", "exec"]
    for k, v in env.items():
        cmd.extend(["-e", f"{k}={v}"])
    cmd.extend(
        [
            CONTAINER,
            PYTHON,
            "-m",
            BENCH,
            "--qdrant-url",
            "http://qdrant:6333",
            "--colbert-url",
            "http://colbert_worker:8082",
            "--files",
            str(FILES),
            "--seed",
            str(SEED),
            "--iterations",
            "20",
            "--output",
            str(out_path),
        ]
    )
    try:
        subprocess.run(cmd, check=True, capture_output=True, text=True, timeout=180)
        raw = subprocess.check_output(
            ["docker", "exec", CONTAINER, "cat", str(out_path)], text=True
        )
        return json.loads(raw)
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired, json.JSONDecodeError) as exc:
        print(f"FAIL {env}: {exc}", file=sys.stderr)
        return None


def score(result: dict) -> float:
    idx = result["indexing"]
    cps = idx["chunks_per_s"]
    rss = idx["peak_rss_mb"]
    # Penalize high RSS (limit 3g MCP) and slow search rerank p95
    rerank_p95 = result.get("lookups_ms", {}).get("search_hybrid_rerank", {}).get("p95", 5.0)
    rss_penalty = max(0, (rss - 500) / 500) * 0.05
    search_penalty = max(0, (rerank_p95 - 5.0) / 10) * 0.02
    return cps * (1.0 - rss_penalty - search_penalty)


def main() -> int:
    phase1_keys = ["FLUSH_EVERY", "COLBERT_EMBED_BATCH_SIZE"]
    phase1_grid = list(
        product(
            [64, 96, 128, 192],
            [16, 32, 48, 64],
        )
    )
    results: list[tuple[dict[str, str], dict, float]] = []

    print("=== Phase 1: FLUSH_EVERY × COLBERT_EMBED_BATCH_SIZE ===")
    for flush_every, colbert_batch in phase1_grid:
        env = {
            "FLUSH_EVERY": str(flush_every),
            "COLBERT_EMBED_BATCH_SIZE": str(colbert_batch),
        }
        print(f"Running {env}...", flush=True)
        result = run_bench(env)
        if result is None:
            continue
        s = score(result)
        idx = result["indexing"]
        print(
            f"  -> {idx['chunks_per_s']} chunks/s, "
            f"full={idx['full_index_s']}s, rss={idx['peak_rss_mb']}MB, score={s:.2f}"
        )
        results.append((env, result, s))
        time.sleep(2)

    if not results:
        print("No successful runs", file=sys.stderr)
        return 1

    results.sort(key=lambda x: x[2], reverse=True)
    best_env, best_result, best_score = results[0]
    print(f"\nPhase 1 best: {best_env} score={best_score:.2f}")

    phase2_base = dict(best_env)
    print("\n=== Phase 2: UPSERT_BATCH × OLLAMA_EMBED_BATCH_SIZE ===")
    phase2_results: list[tuple[dict[str, str], dict, float]] = []
    for upsert, ollama_batch in product([10, 15, 20, 25], [32, 48, 64]):
        env = {
            **phase2_base,
            "UPSERT_BATCH": str(upsert),
            "OLLAMA_EMBED_BATCH_SIZE": str(ollama_batch),
        }
        print(f"Running {env}...", flush=True)
        result = run_bench(env)
        if result is None:
            continue
        s = score(result)
        idx = result["indexing"]
        print(
            f"  -> {idx['chunks_per_s']} chunks/s, "
            f"full={idx['full_index_s']}s, rss={idx['peak_rss_mb']}MB, score={s:.2f}"
        )
        phase2_results.append((env, result, s))
        time.sleep(2)

    all_results = results + phase2_results
    all_results.sort(key=lambda x: x[2], reverse=True)
    winner_env, winner_result, winner_score = all_results[0]

    print("\n=== TOP 5 ===")
    for env, res, sc in all_results[:5]:
        idx = res["indexing"]
        print(
            f"score={sc:.2f}  {idx['chunks_per_s']} c/s  "
            f"full={idx['full_index_s']}s  rss={idx['peak_rss_mb']}MB  {env}"
        )

    report = {
        "winner": winner_env,
        "winner_score": winner_score,
        "winner_indexing": winner_result["indexing"],
        "top5": [
            {"env": e, "score": sc, "indexing": r["indexing"]} for e, r, sc in all_results[:5]
        ],
    }
    report_path = Path(__file__).resolve().parent / "tune_colbert_report.json"
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"\nWrote {report_path}")
    print(f"\nRecommended .env overrides:\n{json.dumps(winner_env, indent=2)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
