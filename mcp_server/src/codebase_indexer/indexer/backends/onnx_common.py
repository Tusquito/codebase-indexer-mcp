"""Helpers shared by the in-process sparse BM25 backend."""

from __future__ import annotations

import os
from typing import Any


def resolve_threads(explicit: int) -> int:
    """Resolve ONNX thread count: explicit value, else OMP env, else auto."""
    if explicit and explicit > 0:
        return explicit
    env = os.environ.get("OMP_NUM_THREADS")
    if env:
        try:
            return max(1, int(env))
        except ValueError:
            pass
    cpu = os.cpu_count() or 8
    return max(1, int(cpu * 0.75))


def extract_onnx_inner(fastembed_wrapper: Any) -> Any | None:
    """Return the underlying ONNX/BM25 implementation from a fastembed wrapper."""
    return getattr(fastembed_wrapper, "model", None)


def extract_tokenizer(fastembed_wrapper: Any) -> Any | None:
    inner = extract_onnx_inner(fastembed_wrapper)
    if inner is None:
        return None
    return getattr(inner, "tokenizer", None)


def extract_model_dir(fastembed_wrapper: Any) -> Any:
    inner = extract_onnx_inner(fastembed_wrapper)
    if inner is None:
        return None
    return getattr(inner, "_model_dir", None)
