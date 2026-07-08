"""Tests for two-hop multi_hop evaluation harness (ADR 0009)."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import patch

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks._connectivity import qdrant_reachable, tei_reachable  # noqa: E402
from benchmarks.eval_multihop import (  # noqa: E402
    filter_multihop_entries,
    render_table,
    run_multihop_evaluation,
)
from benchmarks.eval_retrieval import GoldenEntry, load_golden  # noqa: E402
from codebase_indexer.storage.qdrant import SearchResult  # noqa: E402

GOLDEN = Path(__file__).resolve().parents[1] / "benchmarks" / "fixtures" / "golden_queries.jsonl"
QDRANT_URL = "http://localhost:6333"
TEI_URL = "http://localhost:8080"


def _result(chunk_id: str) -> SearchResult:
    return SearchResult(
        chunk_id=chunk_id,
        score=0.9,
        rel_path="a.py",
        language="python",
        start_line=1,
        end_line=10,
        symbol_name="fn",
        symbol_type="function",
        content="x",
        collection="codebase-indexer-mcp",
    )


def test_golden_multihop_entries_have_hop2_query_text():
    entries = load_golden(GOLDEN)
    multi = filter_multihop_entries(entries)
    assert len(multi) >= 15
    assert all(e.hop2_query_text and e.hop2_query_text.strip() for e in multi)


def test_filter_multihop_raises_when_hop2_missing():
    entries = [
        GoldenEntry(
            query_id="q1",
            query_text="q",
            collection="proj",
            labels={},
            tags=["multi_hop"],
            hop2_query_text=None,
        )
    ]
    with pytest.raises(ValueError, match="hop2_query_text"):
        filter_multihop_entries(entries)


@pytest.mark.asyncio
async def test_run_multihop_evaluation_mocked():
    pytest.importorskip("ranx")
    entries = filter_multihop_entries(load_golden(GOLDEN))
    entry = entries[0]
    labels = {"chunk_a": 2, "chunk_b": 1}

    async def fake_run_search(*, query: str, **kwargs):
        if query == entry.query_text:
            return [_result("chunk_a"), _result("miss1")]
        return [_result("chunk_b"), _result("miss2")]

    with (
        patch("benchmarks.eval_multihop.run_search", side_effect=fake_run_search),
        patch("benchmarks.eval_multihop.resolve_labels", return_value=labels),
        patch("benchmarks.eval_multihop.load_golden", return_value=[entry]),
        patch("benchmarks.eval_multihop.create_backends", return_value=(None, None)),
        patch("benchmarks.eval_multihop.create_colbert_backend", return_value=None),
        patch("benchmarks.eval_multihop.Embedder"),
        patch("benchmarks.eval_multihop.QdrantStorage"),
    ):
        result = await run_multihop_evaluation(
            qdrant_url=QDRANT_URL,
            tei_url=TEI_URL,
            golden_path=GOLDEN,
            hybrid_search=True,
            rerank_enabled=False,
            top_k=10,
            collection_override=None,
        )

    assert result["n_queries"] == 1
    assert result["metrics_two_hop"]["recall@10"] >= result["metrics_single_pass"]["recall@10"]


def test_render_table_includes_both_metric_blocks():
    result = {
        "n_queries": 4,
        "params": {
            "hybrid_search": True,
            "rerank_enabled": False,
            "top_k": 10,
            "rrf_k": 60,
        },
        "metrics_single_pass": {"recall@10": 0.5, "mrr": 0.3, "ndcg@10": 0.35},
        "metrics_two_hop": {"recall@10": 0.75, "mrr": 0.5, "ndcg@10": 0.55},
    }
    table = render_table(result)
    assert "Single-pass" in table
    assert "Two-hop RRF fused" in table
    assert "0.7500" in table


@pytest.mark.benchmark
@pytest.mark.asyncio
@pytest.mark.skipif(not qdrant_reachable(QDRANT_URL), reason="Qdrant not reachable")
@pytest.mark.skipif(not tei_reachable(TEI_URL), reason="TEI not reachable")
async def test_eval_multihop_smoke_on_indexed_collection():
    from codebase_indexer.config import Settings
    from codebase_indexer.storage.qdrant import QdrantStorage

    storage = QdrantStorage(Settings(qdrant_url=QDRANT_URL))
    client = await storage._get_client()
    collections = await client.get_collections()
    names = {c.name for c in collections.collections}
    if "codebase-indexer-mcp" not in names:
        pytest.skip("codebase-indexer-mcp collection not indexed")

    result = await run_multihop_evaluation(
        qdrant_url=QDRANT_URL,
        tei_url=TEI_URL,
        golden_path=GOLDEN,
        hybrid_search=True,
        rerank_enabled=False,
        top_k=10,
        collection_override=None,
    )
    assert result["n_queries"] >= 15
    assert result["metrics_two_hop"]["recall@10"] >= 0.0
