"""Tests for golden-set retrieval evaluation harness (ADR 0007)."""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks._connectivity import ollama_reachable, qdrant_reachable  # noqa: E402
from benchmarks.eval_retrieval import (  # noqa: E402
    GoldenEntry,
    build_run_dict,
    compare,
    load_golden,
    resolve_labels,
    run_evaluation,
    score_for_ranx,
)
from codebase_indexer.indexer.chunker import _make_chunk_id  # noqa: E402
from codebase_indexer.storage.qdrant import SearchResult  # noqa: E402

GOLDEN = Path(__file__).resolve().parents[1] / "benchmarks" / "fixtures" / "golden_queries.jsonl"
QDRANT_URL = "http://localhost:6333"
OLLAMA_URL = "http://localhost:11434"


def _result(chunk_id: str, score: float = 0.9) -> SearchResult:
    return SearchResult(
        chunk_id=chunk_id,
        score=score,
        rel_path="a.py",
        language="python",
        start_line=1,
        end_line=10,
        symbol_name="fn",
        symbol_type="function",
        content="x",
        collection="test",
    )


def test_load_golden_fixture():
    entries = load_golden(GOLDEN)
    assert len(entries) >= 22
    assert entries[0].query_id
    assert entries[0].collection == "codebase-indexer-mcp"


def test_resolve_labels_from_alias():
    entry = GoldenEntry(
        query_id="q1",
        query_text="test",
        collection="codebase-indexer-mcp",
        labels={},
        aliases={"mcp_server/src/foo.py:42": 2},
    )
    labels = resolve_labels(entry)
    expected = _make_chunk_id("codebase-indexer-mcp/mcp_server/src/foo.py", 42)
    assert labels[expected] == 2


def test_score_for_ranx_decreases_with_rank():
    assert score_for_ranx(0, 10) == 10.0
    assert score_for_ranx(9, 10) == 1.0


def test_build_run_dict_uses_rank_scores():
    results = [_result("a"), _result("b"), _result("c")]
    run = build_run_dict(results, top_k=10)
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

    run_miss = {"q1": {"doc_c": 10.0, "doc_a": 5.0}}
    metrics_miss = evaluate(qrels, run_miss, ["recall@1", "mrr"])
    assert float(metrics_miss["recall@1"]) == 0.0


def test_ranx_three_query_fixture():
    pytest.importorskip("ranx")
    from ranx import evaluate

    qrels = {
        "q1": {"a": 2, "b": 1},
        "q2": {"c": 1},
        "q3": {"d": 1, "e": 1},
    }
    run = {
        "q1": {"a": 10.0, "x": 5.0},
        "q2": {"c": 10.0},
        "q3": {"x": 10.0, "d": 5.0},
    }
    metrics = evaluate(qrels, run, ["recall@10", "mrr", "ndcg@10"])
    assert float(metrics["recall@10"]) == pytest.approx(2 / 3, rel=1e-3)
    assert float(metrics["mrr"]) == pytest.approx((1.0 + 1.0 + 0.5) / 3, rel=1e-3)


def test_compute_tag_metrics():
    pytest.importorskip("ranx")
    from benchmarks.eval_retrieval import compute_tag_metrics

    entries = [
        GoldenEntry(
            query_id="q1",
            query_text="symbol query",
            collection="proj",
            labels={},
            tags=["symbol"],
            aliases={"src/a.py:1": 1},
        ),
        GoldenEntry(
            query_id="q2",
            query_text="config query",
            collection="proj",
            labels={},
            tags=["config"],
            aliases={"src/b.py:1": 1},
        ),
    ]
    cid_a = _make_chunk_id("proj/src/a.py", 1)
    cid_b = _make_chunk_id("proj/src/b.py", 1)
    qrels = {"q1": {cid_a: 1}, "q2": {cid_b: 1}}
    run = {"q1": {cid_a: 10.0}, "q2": {"miss": 10.0}}
    by_tag = compute_tag_metrics(entries, qrels, run)
    assert by_tag["symbol"]["recall@10"] == 1.0
    assert by_tag["config"]["recall@10"] == 0.0


@pytest.mark.benchmark
@pytest.mark.asyncio
@pytest.mark.skipif(not qdrant_reachable(QDRANT_URL), reason="Qdrant not reachable")
@pytest.mark.skipif(not ollama_reachable(OLLAMA_URL), reason="Ollama not reachable")
async def test_eval_smoke_on_indexed_collection():
    """Smoke test when codebase-indexer-mcp is indexed locally."""
    from codebase_indexer.config import Settings
    from codebase_indexer.storage.qdrant import QdrantStorage

    storage = QdrantStorage(Settings(qdrant_url=QDRANT_URL))
    client = await storage._get_client()
    collections = await client.get_collections()
    names = {c.name for c in collections.collections}
    if "codebase-indexer-mcp" not in names:
        pytest.skip("codebase-indexer-mcp collection not indexed")

    result = await run_evaluation(
        qdrant_url=QDRANT_URL,
        ollama_url=OLLAMA_URL,
        golden_path=GOLDEN,
        hybrid_search=True,
        top_k=10,
        collection_override=None,
    )
    assert result["n_queries"] >= 15
    assert result["metrics"]["recall@10"] > 0.0


def test_golden_fixture_has_ground_truth_subset():
    entries = load_golden(GOLDEN)
    with_gt = [e for e in entries if e.ground_truth]
    assert len(with_gt) >= 6
    assert all(e.ground_truth.strip() for e in with_gt)


def test_golden_fixture_has_multi_hop_queries():
    entries = load_golden(GOLDEN)
    multi = [e for e in entries if "multi_hop" in e.tags]
    assert len(multi) >= 4


def test_golden_fixture_is_valid_jsonl():
    for line in GOLDEN.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        data = json.loads(line)
        assert "query_id" in data
        assert "query_text" in data
        assert "collection" in data
        assert "labels" in data or "aliases" in data
