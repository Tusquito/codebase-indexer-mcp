"""Benchmark index throughput with remote ColBERT sidecar (CPU vs GPU).

Runs the full pipeline via ``run_benchmark(..., rerank_enabled=True)`` with
``COLBERT_EMBED_BACKEND=remote``. Sidecar ``/health`` device metadata is
recorded in the result JSON for CPU vs GPU comparisons.

Usage:
    python -m benchmarks.bench_colbert_sidecar --output cpu-sidecar.json
    python -m benchmarks.bench_colbert_sidecar --output gpu-sidecar.json
    python -m benchmarks.bench_colbert_sidecar --compare cpu-sidecar.json gpu-sidecar.json
"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from benchmarks._connectivity import (  # noqa: E402
    colbert_health,
    colbert_reachable,
    ollama_reachable,
    qdrant_reachable,
)
from benchmarks.bench import compare, render_table, run_benchmark  # noqa: E402


def _compare_sidecars(
    current: dict[str, object], baseline: dict[str, object], threshold_pct: float
) -> tuple[str, bool]:
    cur_label = current.get("params", {}).get("colbert_sidecar_device", "current")
    base_label = baseline.get("params", {}).get("colbert_sidecar_device", "baseline")
    report, regressed = compare(current, baseline, threshold_pct)  # type: ignore[arg-type]
    header = f"\nColBERT sidecar: {base_label} -> {cur_label}"
    return header + report, regressed


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Benchmark remote ColBERT sidecar index throughput"
    )
    parser.add_argument("--qdrant-url", default=os.environ.get("QDRANT_URL", "http://localhost:6333"))
    parser.add_argument("--colbert-url", default=os.environ.get("COLBERT_URL", "http://localhost:8082"))
    parser.add_argument("--files", type=int, default=int(os.environ.get("BENCH_FILES", "300")))
    parser.add_argument("--seed", type=int, default=int(os.environ.get("BENCH_SEED", "1234")))
    parser.add_argument("--iterations", type=int, default=int(os.environ.get("BENCH_ITERS", "50")))
    parser.add_argument(
        "--collection",
        default=os.environ.get("BENCH_COLLECTION", "bench_colbert_sidecar"),
    )
    parser.add_argument("--keep", action="store_true", help="Keep the benchmark collection afterwards.")
    parser.add_argument("--output", help="Write results JSON to this path.")
    parser.add_argument(
        "--compare",
        nargs=2,
        metavar=("BASELINE", "CURRENT"),
        help="Compare two sidecar benchmark JSON files (e.g. CPU vs GPU).",
    )
    parser.add_argument(
        "--threshold",
        type=float,
        default=float(os.environ.get("BENCH_THRESHOLD", "0")),
        help="Fail if a metric regresses by more than this percent. 0 disables.",
    )
    args = parser.parse_args()

    if args.compare:
        baseline = json.loads(Path(args.compare[0]).read_text(encoding="utf-8"))
        current = json.loads(Path(args.compare[1]).read_text(encoding="utf-8"))
        report, regressed = _compare_sidecars(current, baseline, args.threshold or 1e9)
        print(report)
        if args.threshold and regressed:
            print("FAIL: one or more metrics regressed beyond threshold", file=sys.stderr)
            return 1
        return 0

    if not qdrant_reachable(args.qdrant_url):
        print(f"SKIP: Qdrant not reachable at {args.qdrant_url}", file=sys.stderr)
        if args.output:
            Path(args.output).write_text(
                json.dumps({"skipped": True, "reason": "qdrant_unreachable"}),
                encoding="utf-8",
            )
        return 0

    ollama_url = os.environ.get("OLLAMA_URL", "http://localhost:11434")
    if not ollama_reachable(ollama_url):
        print(f"SKIP: Ollama not reachable at {ollama_url}", file=sys.stderr)
        if args.output:
            Path(args.output).write_text(
                json.dumps({"skipped": True, "reason": "ollama_unreachable"}),
                encoding="utf-8",
            )
        return 0

    if not colbert_reachable(args.colbert_url):
        print(f"SKIP: ColBERT sidecar not reachable at {args.colbert_url}", file=sys.stderr)
        if args.output:
            Path(args.output).write_text(
                json.dumps({"skipped": True, "reason": "colbert_unreachable"}),
                encoding="utf-8",
            )
        return 0

    sidecar_health = colbert_health(args.colbert_url)

    result = asyncio.run(
        run_benchmark(
            qdrant_url=args.qdrant_url,
            files=args.files,
            seed=args.seed,
            iterations=args.iterations,
            collection=args.collection,
            payload_indexes=True,
            rerank_enabled=True,
            keep=args.keep,
            colbert_url=args.colbert_url,
            colbert_embed_backend="remote",
            colbert_sidecar_health=sidecar_health,
        )
    )

    print(render_table(result))
    if sidecar_health:
        print(
            f"\nColBERT sidecar: device={sidecar_health.get('device')} "
            f"cuda_available={sidecar_health.get('cuda_available')}"
        )

    if args.output:
        Path(args.output).write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(f"\nWrote {args.output}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
