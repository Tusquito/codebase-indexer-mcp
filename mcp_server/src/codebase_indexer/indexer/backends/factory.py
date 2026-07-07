"""Factory for embedding backends."""

from __future__ import annotations

from codebase_indexer.config import (
    KNOWN_EMBED_MODEL_MAX_TOKENS,
    Settings,
    tei_embed_dimensions,
)
from codebase_indexer.indexer.backends.base import (
    DenseEmbedBackend,
    LateInteractionEmbedBackend,
    SparseEmbedBackend,
)


def create_dense_backend(settings: Settings) -> DenseEmbedBackend:
    from codebase_indexer.indexer.backends.tei_dense import TeiDenseBackend

    return TeiDenseBackend(
        model_name=settings.dense_embed_model,
        vector_size=settings.dense_embed_vector_size,
        tei_url=settings.tei_url,
        batch_size=settings.tei_embed_batch_size,
        timeout=float(settings.tei_timeout),
        max_dense_embed_tokens=settings.max_dense_embed_tokens,
        dense_embed_model=settings.dense_embed_model,
        known_max_tokens=KNOWN_EMBED_MODEL_MAX_TOKENS,
        mrl_dimensions=tei_embed_dimensions(
            settings.dense_embed_model, settings.dense_embed_vector_size
        ),
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
    """Create TEI dense + in-process sparse ONNX backends."""
    return create_dense_backend(settings), create_sparse_backend(settings)
