"""Pytest wrapper around the benchmark harness.

Runs a small benchmark and asserts structural sanity (not absolute latency,
which is runner-dependent and flaky). Marked ``benchmark`` so it is excluded
from the default ``pytest`` run (see pyproject addopts) and skipped entirely
when no Qdrant is reachable. Run explicitly with ``pytest -m benchmark``.
"""

import os
import sys
from pathlib import Path

import pytest

# benchmarks/ is a top-level package next to tests/, not an installed module.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks._connectivity import qdrant_reachable  # noqa: E402
from benchmarks.bench import compare, run_benchmark  # noqa: E402

QDRANT_URL = os.environ.get("QDRANT_URL", "http://localhost:6333")

pytestmark = [
    pytest.mark.benchmark,
    pytest.mark.skipif(not qdrant_reachable(QDRANT_URL), reason="Qdrant not reachable"),
]


@pytest.mark.asyncio
async def test_benchmark_runs_and_is_self_consistent():
    result = await run_benchmark(
        qdrant_url=QDRANT_URL,
        files=30,
        seed=1234,
        iterations=3,
        collection="benchtest_ci",
        payload_indexes=True,
        keep=False,
    )

    # Indexing produced chunks and the incremental pass skipped (much) faster.
    assert result["indexing"]["total_chunks"] > 0
    assert result["indexing"]["incremental_s"] < result["indexing"]["full_index_s"]
    assert result["indexing"]["incremental_skipped"] == result["corpus"]["n_files"]

    # Every lookup metric is present with non-negative percentiles.
    for name in (
        "get_chunk_by_id",
        "scroll_file_symbols",
        "find_symbol_in_collections",
        "search_hybrid",
        "search_language_filtered",
    ):
        stats = result["lookups_ms"][name]
        assert stats["p50"] >= 0 and stats["p95"] >= stats["p50"]

    assert result["delete_by_paths_ms"]["elapsed_ms"] >= 0


def test_compare_reports_no_regression_against_itself():
    metrics = {
        "indexing": {"chunks_per_s": 100.0, "full_index_s": 10.0},
        "lookups_ms": {"get_chunk_by_id": {"p50": 1.0, "p95": 2.0}},
        "delete_by_paths_ms": {"batch_size": 10, "elapsed_ms": 5.0},
    }
    _report, regressed = compare(metrics, metrics, threshold_pct=10.0)
    assert regressed is False
