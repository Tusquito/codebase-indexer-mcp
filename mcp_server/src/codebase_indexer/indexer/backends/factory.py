"""Factory for embedding backends."""

from __future__ import annotations

from codebase_indexer.config import KNOWN_EMBED_MODEL_MAX_TOKENS, Settings
from codebase_indexer.indexer.backends.base import (
    DenseEmbedBackend,
    LateInteractionEmbedBackend,
    SparseEmbedBackend,
)


def _ollama_model_name(settings: Settings) -> str:
    if settings.ollama_embed_model:
        return settings.ollama_embed_model
    name = settings.dense_embed_model
    if "/" in name:
        return name.rsplit("/", 1)[-1]
    return name


def create_dense_backend(settings: Settings) -> DenseEmbedBackend:
    from codebase_indexer.indexer.backends.ollama_dense import OllamaDenseBackend

    return OllamaDenseBackend(
        model_name=_ollama_model_name(settings),
        vector_size=settings.dense_embed_vector_size,
        ollama_url=settings.ollama_url,
        batch_size=settings.ollama_embed_batch_size,
        timeout=float(settings.ollama_timeout),
        max_dense_embed_tokens=settings.max_dense_embed_tokens,
        dense_embed_model=settings.dense_embed_model,
        known_max_tokens=KNOWN_EMBED_MODEL_MAX_TOKENS,
    )


def create_sparse_backend(settings: Settings) -> SparseEmbedBackend:
    from codebase_indexer.indexer.backends.onnx_sparse import OnnxSparseBackend

    return OnnxSparseBackend(
        model_name=settings.sparse_embed_model,
        sparse_threads=settings.sparse_threads,
        max_sparse_embed_tokens=settings.max_sparse_embed_tokens,
    )


def create_colbert_backend(settings: Settings) -> LateInteractionEmbedBackend:
    if settings.colbert_embed_backend == "remote":
        from codebase_indexer.indexer.backends.colbert_remote import ColbertRemoteBackend

        return ColbertRemoteBackend(
            model_name=settings.colbert_embed_model,
            colbert_url=settings.colbert_url,
            batch_size=settings.colbert_embed_batch_size,
            timeout=float(settings.colbert_timeout),
        )

    from codebase_indexer.indexer.backends.colbert_onnx import ColbertOnnxBackend

    return ColbertOnnxBackend(
        model_name=settings.colbert_embed_model,
        sparse_threads=settings.sparse_threads,
        max_query_tokens=settings.rerank_max_query_tokens,
    )


def create_backends(
    settings: Settings,
) -> tuple[DenseEmbedBackend, SparseEmbedBackend]:
    """Create Ollama dense + in-process sparse ONNX backends."""
    return create_dense_backend(settings), create_sparse_backend(settings)
