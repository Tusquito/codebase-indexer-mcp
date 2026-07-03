"""Unit tests for hard-negative mining (ADR 0020)."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import MagicMock

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks.eval_retrieval import GoldenEntry  # noqa: E402
from benchmarks.train._schema import TrainingPair  # noqa: E402
from benchmarks.train.mine_hard_negatives import (  # noqa: E402
    mine_hard_negatives,
    mine_negatives_for_pair,
)
from codebase_indexer.storage.qdrant import SearchResult  # noqa: E402


def _result(chunk_id: str, content: str) -> SearchResult:
    return SearchResult(
        chunk_id=chunk_id,
        score=0.9,
        rel_path="a.py",
        language="python",
        start_line=1,
        end_line=5,
        symbol_name="fn",
        symbol_type="function",
        content=content,
        collection="test",
    )


@pytest.mark.asyncio
async def test_mine_negatives_excludes_labeled_chunks(monkeypatch: pytest.MonkeyPatch):
    entry = GoldenEntry(
        query_id="q_test",
        query_text="embedder",
        collection="codebase-indexer-mcp",
        labels={},
        aliases={"mcp_server/src/codebase_indexer/indexer/embedder.py:45": 2},
    )
    from codebase_indexer.indexer.chunker import _make_chunk_id

    rel = "codebase-indexer-mcp/mcp_server/src/codebase_indexer/indexer/embedder.py"
    labeled_id = _make_chunk_id(rel, 45)
    miss_id = "miss-chunk"

    async def fake_run_search(**kwargs):
        return [
            _result(labeled_id, "labeled content"),
            _result(miss_id, "hard negative passage"),
            _result("dup", "hard negative passage"),
        ]

    monkeypatch.setattr(
        "benchmarks.train.mine_hard_negatives.run_search",
        fake_run_search,
    )

    pair = TrainingPair(query_id="q_test", query="embedder", positive="pos")
    negatives = await mine_negatives_for_pair(
        pair,
        entry,
        storage=MagicMock(),
        embedder=MagicMock(),
        top_k=10,
        collection="codebase-indexer-mcp",
    )
    assert negatives == ["hard negative passage"]


@pytest.mark.asyncio
async def test_mine_hard_negatives_updates_pairs(monkeypatch: pytest.MonkeyPatch):
    pair = TrainingPair(query_id="q_ok", query="q", positive="p")
    entry = GoldenEntry(
        query_id="q_ok",
        query_text="q",
        collection="coll",
        labels={"x": 1},
    )
    golden_by_id = {"q_ok": entry}

    async def fake_mine(*args, **kwargs):
        return ["neg-a", "neg-b"]

    monkeypatch.setattr(
        "benchmarks.train.mine_hard_negatives.mine_negatives_for_pair",
        fake_mine,
    )

    updated = await mine_hard_negatives(
        [pair],
        golden_by_id,
        storage=MagicMock(),
        embedder=MagicMock(),
        top_k=5,
        collection_override=None,
    )
    assert updated[0].negatives == ["neg-a", "neg-b"]


@pytest.mark.asyncio
async def test_mine_skips_unknown_query_id():
    pair = TrainingPair(query_id="q_unknown", query="q", positive="p")
    updated = await mine_hard_negatives(
        [pair],
        {},
        storage=MagicMock(),
        embedder=MagicMock(),
        top_k=5,
        collection_override=None,
    )
    assert updated[0].negatives == []
