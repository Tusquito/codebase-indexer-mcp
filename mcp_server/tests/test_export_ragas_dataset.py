"""Tests for Ragas golden-set export (ADR 0010)."""

from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks.eval_retrieval import load_golden  # noqa: E402
from benchmarks.export_ragas_dataset import export_rows  # noqa: E402

GOLDEN = Path(__file__).resolve().parents[1] / "benchmarks" / "fixtures" / "golden_queries.jsonl"


def test_export_rows_includes_metadata():
    entries = load_golden(GOLDEN)
    rows = export_rows(entries)
    assert len(rows) == len(entries)
    row = next(r for r in rows if r["query_id"] == "q_make_chunk_id")
    assert row["question"]
    assert row["collection"] == "codebase-indexer-mcp"
    assert "ground_truth" in row


def test_export_rows_require_ground_truth_filters():
    entries = load_golden(GOLDEN)
    all_rows = export_rows(entries)
    gt_rows = export_rows(entries, require_ground_truth=True)
    assert len(gt_rows) < len(all_rows)
    assert all("ground_truth" in r for r in gt_rows)


def test_export_cli_fixture_roundtrip(tmp_path: Path):
    entries = load_golden(GOLDEN)
    out = tmp_path / "ragas.json"
    rows = export_rows(entries, require_ground_truth=True)
    out.write_text(json.dumps(rows, indent=2) + "\n", encoding="utf-8")
    loaded = json.loads(out.read_text(encoding="utf-8"))
    assert loaded[0]["query_id"]
    assert loaded[0]["question"]
