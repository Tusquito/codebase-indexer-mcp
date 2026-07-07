"""Tests for embedding backend factory."""

from codebase_indexer.config import Settings
from codebase_indexer.indexer.backends.factory import (
    create_backends,
    create_colbert_backend,
    create_dense_backend,
    create_sparse_backend,
)
from codebase_indexer.indexer.backends.colbert_remote import ColbertRemoteBackend
from codebase_indexer.indexer.backends.onnx_sparse import OnnxSparseBackend
from codebase_indexer.indexer.backends.tei_dense import TeiDenseBackend


def test_factory_uses_tei_dense():
    settings = Settings()
    dense = create_dense_backend(settings)
    sparse = create_sparse_backend(settings)
    assert isinstance(dense, TeiDenseBackend)
    assert isinstance(sparse, OnnxSparseBackend)
    assert dense.model_name == settings.dense_embed_model


def test_create_backends_returns_dense_and_sparse():
    settings = Settings()
    dense, sparse = create_backends(settings)
    assert isinstance(dense, TeiDenseBackend)
    assert isinstance(sparse, OnnxSparseBackend)


def test_factory_uses_remote_colbert_by_default_when_rerank_on():
    settings = Settings(rerank_enabled=True)
    assert settings.colbert_embed_backend == "remote"
    backend = create_colbert_backend(settings)
    assert isinstance(backend, ColbertRemoteBackend)
    assert backend.colbert_url == "http://colbert_worker:8082"


def test_factory_uses_remote_colbert_when_configured():
    settings = Settings(
        colbert_embed_backend="remote",
        colbert_url="http://colbert_worker:8082",
    )
    backend = create_colbert_backend(settings)
    assert isinstance(backend, ColbertRemoteBackend)
    assert backend.colbert_url == "http://colbert_worker:8082"
