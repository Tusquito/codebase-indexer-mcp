"""Tests for golden-set retrieval evaluation harness (ADR 0007 / 0030 Phase 7)."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from benchmarks.chunk_id import make_chunk_id
from benchmarks.eval_retrieval import (
    GoldenEntry,
    build_run_dict_from_chunk_ids,
    compare,
    load_golden,
    resolve_labels,
    score_for_ranx,
)

GOLDEN = (
    Path(__file__).resolve().parents[1]
    / "benchmarks"
    / "fixtures"
    / "golden_queries.jsonl"
)


def test_load_golden_fixture():
    entries = load_golden(GOLDEN)
    assert len(entries) >= 20
    assert entries[0].query_id
    assert entries[0].collection == "codebase-indexer-mcp"


def test_resolve_labels_from_alias():
    entry = GoldenEntry(
        query_id="q1",
        query_text="test",
        collection="codebase-indexer-mcp",
        labels={},
        aliases={"src/CodebaseIndexer.Domain/Models/ChunkId.cs:17": 2},
    )
    labels = resolve_labels(entry)
    expected = make_chunk_id(
        "codebase-indexer-mcp/src/CodebaseIndexer.Domain/Models/ChunkId.cs", 17
    )
    assert labels[expected] == 2


def test_score_for_ranx_decreases_with_rank():
    assert score_for_ranx(0, 10) == 10.0
    assert score_for_ranx(9, 10) == 1.0


def test_build_run_dict_from_chunk_ids():
    run = build_run_dict_from_chunk_ids(["a", "b", "c"], top_k=10)
    assert run["a"] == 10.0
    assert run["b"] == 9.0
    assert run["c"] == 8.0


def test_compare_detects_regression():
    baseline = {"metrics": {"recall@10": 0.8, "mrr": 0.5, "ndcg@10": 0.6}}
    current = {"metrics": {"recall@10": 0.5, "mrr": 0.5, "ndcg@10": 0.6}}
    _report, regressed = compare(current, baseline, threshold_pct=10.0)
    assert regressed is True


def test_compare_no_regression_against_self():
    metrics = {"metrics": {"recall@10": 0.7, "mrr": 0.4, "ndcg@10": 0.55}}
    _report, regressed = compare(metrics, metrics, threshold_pct=5.0)
    assert regressed is False


def test_ranx_recall_at_1_hand_calculated():
    pytest.importorskip("ranx")
    from ranx import evaluate

    qrels = {"q1": {"doc_a": 1}}
    run = {"q1": {"doc_a": 10.0, "doc_c": 5.0}}
    metrics = evaluate(qrels, run, ["recall@1", "mrr"])
    assert float(metrics["recall@1"]) == 1.0
    assert float(metrics["mrr"]) == 1.0


def test_chunk_id_sha256_parity():
    cid = make_chunk_id("proj/a.cs", 42)
    assert len(cid) == 64
    assert cid == make_chunk_id("proj/a.cs", 42)
    assert cid != make_chunk_id("proj/a.cs", 43)


def test_golden_fixture_is_valid_jsonl():
    for line in GOLDEN.read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        data = json.loads(line)
        assert "query_id" in data and "query_text" in data
