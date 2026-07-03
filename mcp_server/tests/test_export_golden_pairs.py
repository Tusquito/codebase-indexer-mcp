"""Unit tests for golden-pair export (ADR 0020)."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks.eval_retrieval import GoldenEntry, load_golden  # noqa: E402
from benchmarks.train._positives import resolve_positive_passage  # noqa: E402
from benchmarks.train._schema import read_jsonl  # noqa: E402
from benchmarks.train.export_golden_pairs import export_pairs  # noqa: E402

GOLDEN = Path(__file__).resolve().parents[1] / "benchmarks" / "fixtures" / "golden_queries.jsonl"


@pytest.mark.asyncio
async def test_resolve_positive_picks_highest_grade_alias():
    storage = MagicMock()
    storage.get_chunk_by_id = AsyncMock(
        return_value={"content": "class Embedder:\n    ...", "chunk_id": "abc"}
    )
    entry = GoldenEntry(
        query_id="q_test",
        query_text="embedder",
        collection="codebase-indexer-mcp",
        labels={},
        aliases={"mcp_server/src/codebase_indexer/indexer/embedder.py:45": 2},
    )
    text = await resolve_positive_passage(storage, entry)
    assert "Embedder" in text
    storage.get_chunk_by_id.assert_awaited()


@pytest.mark.asyncio
async def test_export_pairs_success_and_missing():
    storage = MagicMock()
    good = GoldenEntry(
        query_id="q_ok",
        query_text="query",
        collection="coll",
        labels={"chunk-1": 2},
        tags=["symbol"],
    )
    bad = GoldenEntry(
        query_id="q_bad",
        query_text="missing",
        collection="coll",
        labels={"missing-chunk": 1},
    )

    async def _get_chunk(collection: str, chunk_id: str):
        if chunk_id == "chunk-1":
            return {"content": "positive text"}
        return None

    storage.get_chunk_by_id = AsyncMock(side_effect=_get_chunk)

    pairs, errors = await export_pairs([good, bad], storage)
    assert len(pairs) == 1
    assert pairs[0].query_id == "q_ok"
    assert pairs[0].positive == "positive text"
    assert pairs[0].tags == ["symbol"]
    assert len(errors) == 1
    assert "q_bad" in errors[0]


@pytest.mark.asyncio
async def test_export_pairs_jsonl_roundtrip(tmp_path: Path):
    from benchmarks.train._schema import TrainingPair, write_jsonl  # noqa: E402

    pair = TrainingPair(query_id="q1", query="q", positive="p", tags=["config"])
    out = tmp_path / "out.jsonl"
    write_jsonl([pair], out)
    assert read_jsonl(out)[0].query_id == "q1"


def test_golden_fixture_loads():
    entries = load_golden(GOLDEN)
    assert any(e.query_id == "q_embedder_class" for e in entries)
