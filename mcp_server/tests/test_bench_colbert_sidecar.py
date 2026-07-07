"""Tests for benchmarks/bench_colbert_sidecar.py."""

from __future__ import annotations

import json
import sys
from unittest.mock import AsyncMock, patch

from benchmarks.bench_colbert_sidecar import main


def test_bench_colbert_sidecar_skips_when_sidecar_unreachable(tmp_path):
    out = tmp_path / "skipped.json"
    argv = [
        "bench_colbert_sidecar",
        "--output",
        str(out),
    ]
    with patch("benchmarks.bench_colbert_sidecar.qdrant_reachable", return_value=True), patch(
        "benchmarks.bench_colbert_sidecar.tei_reachable", return_value=True
    ), patch("benchmarks.bench_colbert_sidecar.colbert_reachable", return_value=False), patch.object(
        sys, "argv", argv
    ):
        rc = main()

    assert rc == 0
    data = json.loads(out.read_text(encoding="utf-8"))
    assert data["skipped"] is True
    assert data["reason"] == "colbert_unreachable"


def test_bench_colbert_sidecar_runs_with_remote_colbert(tmp_path):
    fake_result = {
        "schema": 1,
        "params": {
            "files": 10,
            "seed": 1234,
            "iterations": 1,
            "payload_indexes": True,
            "rerank_enabled": True,
            "colbert_embed_backend": "remote",
            "colbert_url": "http://localhost:8082",
            "colbert_sidecar_device": "cuda",
            "colbert_sidecar_cuda_available": True,
        },
        "indexing": {
            "full_index_s": 1.0,
            "incremental_s": 0.1,
            "total_chunks": 10,
            "indexed_files": 5,
            "chunks_per_s": 10.0,
            "incremental_skipped": 5,
            "peak_rss_mb": 100,
        },
        "lookups_ms": {},
        "delete_by_paths_ms": {"batch_size": 1, "elapsed_ms": 1.0},
    }
    out = tmp_path / "result.json"
    argv = [
        "bench_colbert_sidecar",
        "--output",
        str(out),
        "--files",
        "10",
        "--iterations",
        "1",
    ]
    with patch("benchmarks.bench_colbert_sidecar.qdrant_reachable", return_value=True), patch(
        "benchmarks.bench_colbert_sidecar.tei_reachable", return_value=True
    ), patch("benchmarks.bench_colbert_sidecar.colbert_reachable", return_value=True), patch(
        "benchmarks.bench_colbert_sidecar.colbert_health",
        return_value={"device": "cuda", "cuda_available": True},
    ), patch(
        "benchmarks.bench_colbert_sidecar.run_benchmark",
        new=AsyncMock(return_value=fake_result),
    ), patch.object(sys, "argv", argv):
        rc = main()

    assert rc == 0
    data = json.loads(out.read_text(encoding="utf-8"))
    assert data["params"]["colbert_sidecar_device"] == "cuda"
