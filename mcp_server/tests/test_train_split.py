"""Unit tests for train/val holdout split (ADR 0020)."""

from __future__ import annotations

import sys
from dataclasses import dataclass, field
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks.eval_retrieval import load_golden  # noqa: E402
from benchmarks.train._split import (  # noqa: E402
    DEFAULT_HOLDOUT_IDS,
    split_holdout,
)

GOLDEN = Path(__file__).resolve().parents[1] / "benchmarks" / "fixtures" / "golden_queries.jsonl"


@dataclass
class _Entry:
    query_id: str
    tags: list[str] = field(default_factory=list)


def test_multi_hop_split_reserves_four_queries():
    entries = load_golden(GOLDEN)
    train, val = split_holdout(entries, strategy="multi_hop")
    val_ids = {e.query_id for e in val}
    assert val_ids == set(DEFAULT_HOLDOUT_IDS)
    assert len(train) + len(val) == len(entries)
    assert not (set(e.query_id for e in train) & val_ids)


def test_holdout_ids_explicit():
    entries = [_Entry("a"), _Entry("b"), _Entry("c")]
    train, val = split_holdout(entries, strategy="holdout_ids", holdout_ids={"b"})
    assert [e.query_id for e in val] == ["b"]
    assert [e.query_id for e in train] == ["a", "c"]


def test_stratified_split_is_reproducible():
    entries = [_Entry(str(i)) for i in range(20)]
    train1, val1 = split_holdout(entries, strategy="stratified", seed=99)
    train2, val2 = split_holdout(entries, strategy="stratified", seed=99)
    assert [e.query_id for e in val1] == [e.query_id for e in val2]
    assert len(val1) == 3  # 15% of 20 rounded
    assert len(train1) == 17


def test_empty_entries():
    train, val = split_holdout([], strategy="multi_hop")
    assert train == []
    assert val == []
