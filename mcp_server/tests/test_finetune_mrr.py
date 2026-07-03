"""Unit tests for validation MRR helper (ADR 0020)."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks.train.finetune_qwen3_code import compute_mrr  # noqa: E402


def test_compute_mrr_perfect_rank():
    q = [[1.0, 0.0]]
    p = [[1.0, 0.0]]
    assert compute_mrr(q, p) == 1.0


def test_compute_mrr_with_hard_negative():
    q = [[1.0, 0.0]]
    p = [[0.0, 1.0]]
    negs = [[[1.0, 0.0]]]  # negative aligns with query better than positive
    mrr = compute_mrr(q, p, negative_embeddings=negs)
    assert mrr == 0.5  # positive ranks second


def test_compute_mrr_empty():
    assert compute_mrr([], []) == 0.0
