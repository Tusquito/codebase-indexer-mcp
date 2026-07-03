"""Unit tests for training pair schema (ADR 0020)."""

from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks.train._schema import TrainingPair, read_jsonl, write_jsonl  # noqa: E402


def test_training_pair_roundtrip_dict():
    pair = TrainingPair(
        query_id="q_test",
        query="find embedder class",
        positive="class Embedder: ...",
        negatives=["wrong chunk"],
        tags=["symbol"],
    )
    restored = TrainingPair.from_dict(pair.to_dict())
    assert restored == pair


def test_write_read_jsonl(tmp_path: Path):
    pairs = [
        TrainingPair(
            query_id="q_a",
            query="query a",
            positive="passage a",
            tags=["conceptual"],
        ),
        TrainingPair(
            query_id="q_b",
            query="query b",
            positive="passage b",
            negatives=["neg1", "neg2"],
            tags=["symbol", "config"],
        ),
    ]
    path = tmp_path / "pairs.jsonl"
    write_jsonl(pairs, path)
    loaded = read_jsonl(path)
    assert loaded == pairs


def test_read_jsonl_skips_comments_and_blanks(tmp_path: Path):
    path = tmp_path / "pairs.jsonl"
    row = {"query_id": "q1", "query": "q", "positive": "p", "negatives": [], "tags": []}
    path.write_text(f"# comment\n\n{json.dumps(row)}\n", encoding="utf-8")
    assert len(read_jsonl(path)) == 1
