"""Async indexer micro-benchmark stub (ADR 0030 Phase 7).

The Python indexer pipeline was removed with the MCP runtime. Use the .NET
project ``src/CodebaseIndexer.Benchmarks`` for latency/throughput benches.
"""

from __future__ import annotations

import argparse
import sys


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Deprecated Python indexer bench — use CodebaseIndexer.Benchmarks"
    )
    parser.add_argument("--compare", help="Ignored (compat)")
    parser.add_argument("--output", help="Ignored (compat)")
    parser.add_argument("--threshold", type=float, default=0.0, help="Ignored (compat)")
    parser.add_argument("--files", type=int, default=0, help="Ignored (compat)")
    parser.add_argument("--iterations", type=int, default=0, help="Ignored (compat)")
    parser.parse_args()
    print(
        "SKIP: Python benchmarks.bench indexer path removed (ADR 0030 Phase 7). "
        "Use `dotnet run --project src/CodebaseIndexer.Benchmarks` instead.",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
