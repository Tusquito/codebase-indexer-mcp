"""Sweep memory-related .env knobs; rank by peak RSS with throughput floor."""

from __future__ import annotations

import json
import re
import subprocess
import sys
import time
from itertools import product
from pathlib import Path

CONTAINER = "codeindexer_mcp"
COLBERT = "codeindexer_colbert"
PYTHON = "/app/.venv/bin/python"
BENCH = "benchmarks.bench_colbert_sidecar"
FILES = 100
SEED = 1234
OUT_DIR = Path("/tmp/tune_rss")

# Throughput-optimized baseline from prior sweep (fixed during RSS tuning).
THROUGHPUT_BASE = {
    "UPSERT_BATCH": "25",
    "TEI_EMBED_BATCH_SIZE": "64",
    "COLBERT_EMBED_BATCH_SIZE": "32",
    "BATCH_SIZE": "32",
}

# Reject configs below this fraction of reference throughput (100-file run).
THROUGHPUT_FLOOR_RATIO = 0.75


def _docker_stats_mb(container: str) -> float | None:
    try:
        out = subprocess.check_output(
            ["docker", "stats", "--no-stream", "--format", "{{.MemUsage}}", container],
            text=True,
            timeout=15,
        ).strip()
        # e.g. "156.2MiB / 3GiB"
        m = re.match(r"([\d.]+)(MiB|GiB)", out)
        if not m:
            return None
        val = float(m.group(1))
        if m.group(2) == "GiB":
            val *= 1024
        return round(val, 1)
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired):
        return None


def run_bench(env: dict[str, str]) -> tuple[dict | None, float, float | None, float | None]:
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
            "10",
            "--output",
            str(out_path),
        ]
    )
    mcp_before = _docker_stats_mb(CONTAINER)
    colbert_before = _docker_stats_mb(COLBERT)
    try:
        proc = subprocess.run(cmd, check=True, capture_output=True, text=True, timeout=180)
        combined = proc.stdout + proc.stderr
        flush_rss = [float(x) for x in re.findall(r"rss_mb=([\d.]+)", combined)]
        peak_flush_rss = max(flush_rss) if flush_rss else 0.0
        raw = subprocess.check_output(
            ["docker", "exec", CONTAINER, "cat", str(out_path)], text=True
        )
        result = json.loads(raw)
        mcp_after = _docker_stats_mb(CONTAINER)
        colbert_after = _docker_stats_mb(COLBERT)
        mcp_peak = max(v for v in (mcp_before, mcp_after, result["indexing"]["peak_rss_mb"]) if v)
        colbert_peak = max(v for v in (colbert_before, colbert_after) if v) if colbert_before else colbert_after
        peak_rss = max(peak_flush_rss, result["indexing"]["peak_rss_mb"])
        return result, peak_rss, mcp_peak, colbert_peak
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired, json.JSONDecodeError) as exc:
        print(f"FAIL {env}: {exc}", file=sys.stderr)
        return None, 0.0, None, None


def score(
    peak_rss: float,
    chunks_per_s: float,
    ref_cps: float,
    mcp_stats: float | None,
    colbert_stats: float | None,
) -> float:
    """Lower is better. Penalize throughput below floor."""
    if ref_cps > 0 and chunks_per_s < ref_cps * THROUGHPUT_FLOOR_RATIO:
        return 1e9
    total = peak_rss
    if mcp_stats:
        total = max(total, mcp_stats)
    # Small throughput bonus so we don't pick absurdly slow configs
    throughput_bonus = min(chunks_per_s / max(ref_cps, 1), 1.0) * 5
    return total - throughput_bonus


def main() -> int:
    print("=== Reference run (throughput-optimized baseline) ===")
    ref_result, ref_peak, ref_mcp, ref_colbert = run_bench(dict(THROUGHPUT_BASE))
    if ref_result is None:
        print("Reference run failed", file=sys.stderr)
        return 1
    ref_cps = ref_result["indexing"]["chunks_per_s"]
    print(
        f"  ref: {ref_cps} c/s, peak_rss={ref_peak}MB, "
        f"mcp_stats={ref_mcp}MB, colbert_stats={ref_colbert}MB"
    )

    phase1: list[tuple[dict[str, str], dict, float, float, float | None, float | None]] = []
    print("\n=== Phase 1: FLUSH_EVERY × SEQUENTIAL_EMBED × MAX_DENSE_EMBED_TOKENS ===")
    for flush_every, sequential, max_tokens in product(
        [32, 48, 64, 96],
        ["false", "true"],
        [512, 768, 1024],
    ):
        env = {
            **THROUGHPUT_BASE,
            "FLUSH_EVERY": str(flush_every),
            "SEQUENTIAL_EMBED": sequential,
            "MAX_DENSE_EMBED_TOKENS": str(max_tokens),
        }
        print(f"Running {env}...", flush=True)
        result, peak_rss, mcp_peak, colbert_peak = run_bench(env)
        if result is None:
            continue
        idx = result["indexing"]
        sc = score(peak_rss, idx["chunks_per_s"], ref_cps, mcp_peak, colbert_peak)
        print(
            f"  -> peak={peak_rss}MB mcp={mcp_peak}MB colbert={colbert_peak}MB "
            f"{idx['chunks_per_s']} c/s score={sc:.1f}"
        )
        phase1.append((env, result, peak_rss, sc, mcp_peak, colbert_peak))
        time.sleep(1)

    phase1.sort(key=lambda x: x[3])
    if not phase1:
        print("No successful runs", file=sys.stderr)
        return 1

    best_env = dict(phase1[0][0])
    print(f"\nPhase 1 best RSS: peak={phase1[0][2]}MB {best_env}")

    print("\n=== Phase 2: COLBERT_EMBED_BATCH_SIZE × BATCH_SIZE on best RSS base ===")
    phase2: list[tuple[dict[str, str], dict, float, float, float | None, float | None]] = []
    for colbert_batch, batch_size in product([16, 24, 32], [16, 24, 32]):
        env = {**best_env, "COLBERT_EMBED_BATCH_SIZE": str(colbert_batch), "BATCH_SIZE": str(batch_size)}
        print(f"Running {env}...", flush=True)
        result, peak_rss, mcp_peak, colbert_peak = run_bench(env)
        if result is None:
            continue
        idx = result["indexing"]
        sc = score(peak_rss, idx["chunks_per_s"], ref_cps, mcp_peak, colbert_peak)
        print(
            f"  -> peak={peak_rss}MB {idx['chunks_per_s']} c/s score={sc:.1f}"
        )
        phase2.append((env, result, peak_rss, sc, mcp_peak, colbert_peak))
        time.sleep(1)

    all_results = sorted(phase1 + phase2, key=lambda x: x[3])
    winner_env, winner_result, winner_peak, winner_score, winner_mcp, winner_colbert = all_results[0]

    # Recommend cgroup limits: 2.5× MCP peak, 1.5× colbert peak, round up
    mcp_limit_mb = int((winner_mcp or winner_peak) * 2.5 / 256 + 1) * 256
    mcp_limit_mb = max(mcp_limit_mb, 512)
    colbert_limit_mb = int((winner_colbert or 512) * 1.5 / 256 + 1) * 256
    colbert_limit_mb = max(colbert_limit_mb, 512)

    def _fmt_mb(n: int) -> str:
        if n >= 1024 and n % 512 == 0:
            return f"{n // 1024}g"
        return f"{n}m"

    print("\n=== TOP 5 (lowest RSS, throughput floor {:.0f}% of ref) ===".format(THROUGHPUT_FLOOR_RATIO * 100))
    for env, res, peak, sc, mcp_p, col_p in all_results[:5]:
        idx = res["indexing"]
        print(
            f"score={sc:.1f} peak={peak}MB mcp={mcp_p}MB colbert={col_p}MB "
            f"{idx['chunks_per_s']} c/s  {env}"
        )

    report = {
        "reference": {
            "env": THROUGHPUT_BASE,
            "chunks_per_s": ref_cps,
            "peak_rss_mb": ref_peak,
            "mcp_stats_mb": ref_mcp,
            "colbert_stats_mb": ref_colbert,
        },
        "winner": {
            "env": winner_env,
            "chunks_per_s": winner_result["indexing"]["chunks_per_s"],
            "peak_rss_mb": winner_peak,
            "mcp_stats_mb": winner_mcp,
            "colbert_stats_mb": winner_colbert,
            "score": winner_score,
        },
        "recommended_compose_limits": {
            "MCP_MEM_LIMIT": _fmt_mb(mcp_limit_mb),
            "COLBERT_MEM_LIMIT": _fmt_mb(colbert_limit_mb),
            "rationale": f"2.5× MCP peak ({winner_mcp or winner_peak}MB), 1.5× ColBERT peak ({winner_colbert}MB)",
        },
        "top5": [
            {
                "env": e,
                "peak_rss_mb": p,
                "mcp_stats_mb": m,
                "colbert_stats_mb": c,
                "chunks_per_s": r["indexing"]["chunks_per_s"],
                "score": s,
            }
            for e, r, p, s, m, c in all_results[:5]
        ],
    }
    report_path = Path(__file__).resolve().parent / "tune_rss_report.json"
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"\nWrote {report_path}")
    print(f"\nRecommended .env tuning:\n{json.dumps(winner_env, indent=2)}")
    print(
        f"\nRecommended compose limits:\n"
        f"  MCP_MEM_LIMIT={_fmt_mb(mcp_limit_mb)}\n"
        f"  COLBERT_MEM_LIMIT={_fmt_mb(colbert_limit_mb)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
