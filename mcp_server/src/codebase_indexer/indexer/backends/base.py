"""Shared types and protocols for embedding backends."""

from __future__ import annotations

import ctypes
import gc
from dataclasses import dataclass
from typing import Protocol, runtime_checkable


class EmbeddingError(Exception):
    """Raised when embedding fails (dimension mismatch, OOM halt, remote error)."""


@dataclass
class SparseVector:
    indices: list[int]
    values: list[float]


def trim_memory() -> None:
    """Return freed native allocations to the OS (Linux/glibc only)."""
    gc.collect()
    try:
        ctypes.CDLL("libc.so.6").malloc_trim(0)
    except Exception:
        pass


@runtime_checkable
class DenseEmbedBackend(Protocol):
    """Dense vector encoder (Ollama HTTP)."""

    vector_size: int
    backend_name: str

    def is_loaded(self) -> bool: ...

    def preload(self) -> None: ...

    def release(self) -> None: ...

    async def embed_batch(self, texts: list[str]) -> list[list[float]]: ...


@runtime_checkable
class SparseEmbedBackend(Protocol):
    """Sparse vector encoder (in-process ONNX BM25)."""

    backend_name: str

    def is_loaded(self) -> bool: ...

    def preload(self) -> None: ...

    def release(self) -> None: ...

    async def embed_batch(self, texts: list[str]) -> list[SparseVector]: ...


@runtime_checkable
class LateInteractionEmbedBackend(Protocol):
    """Late-interaction multivector encoder (ColBERT via fastembed ONNX)."""

    token_dimension: int
    backend_name: str

    def is_loaded(self) -> bool: ...

    def preload(self) -> None: ...

    def release(self) -> None: ...

    async def embed_batch(self, texts: list[str]) -> list[list[list[float]]]: ...
