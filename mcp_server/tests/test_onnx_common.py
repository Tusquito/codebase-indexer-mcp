"""Tests for sparse ONNX helper utilities."""

from codebase_indexer.indexer.backends.onnx_common import resolve_threads


def test_resolve_threads_uses_explicit_value():
    assert resolve_threads(4) == 4


def test_resolve_threads_falls_back_to_auto(monkeypatch):
    monkeypatch.delenv("OMP_NUM_THREADS", raising=False)
    assert resolve_threads(0) >= 1
