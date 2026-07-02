"""Pluggable embedding backends for dense and sparse vector generation."""

from codebase_indexer.indexer.backends.base import (
    DenseEmbedBackend,
    EmbeddingError,
    SparseEmbedBackend,
    SparseVector,
    trim_memory,
)
from codebase_indexer.indexer.backends.factory import (
    create_dense_backend,
    create_sparse_backend,
)

__all__ = [
    "DenseEmbedBackend",
    "SparseEmbedBackend",
    "SparseVector",
    "EmbeddingError",
    "trim_memory",
    "create_dense_backend",
    "create_sparse_backend",
]
