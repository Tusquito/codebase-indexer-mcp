"""Unit tests for client-side multi-hop RRF fusion (ADR 0009)."""

from __future__ import annotations

from codebase_indexer.storage.qdrant import SearchResult

from benchmarks.multihop_rrf import fuse_hop_rrf


def _result(chunk_id: str, score: float = 0.9, collection: str = "proj") -> SearchResult:
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
        collection=collection,
    )


def test_fuse_hop_rrf_boosts_chunks_in_both_hops():
    hop1 = [_result("a"), _result("b"), _result("c")]
    hop2 = [_result("b"), _result("d")]
    fused = fuse_hop_rrf([hop1, hop2], rrf_k=60, top_k=4)
    ids = [r.chunk_id for r in fused]
    assert ids[0] == "b"
    assert set(ids) == {"b", "a", "d", "c"}


def test_fuse_hop_rrf_respects_top_k():
    hop1 = [_result("a"), _result("b")]
    hop2 = [_result("c")]
    fused = fuse_hop_rrf([hop1, hop2], rrf_k=60, top_k=2)
    assert len(fused) == 2


def test_fuse_hop_rrf_empty_input():
    assert fuse_hop_rrf([], rrf_k=60, top_k=5) == []


def test_fuse_hop_rrf_tie_breaks_by_chunk_id():
    hop1 = [_result("a"), _result("b")]
    hop2 = [_result("b"), _result("a")]
    fused = fuse_hop_rrf([hop1, hop2], rrf_k=60, top_k=2)
    assert {r.chunk_id for r in fused} == {"a", "b"}
    assert fused[0].chunk_id == "b"
