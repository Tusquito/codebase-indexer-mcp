"""Tests for embedding backend factory."""

from codebase_indexer.config import Settings
from codebase_indexer.indexer.backends.factory import (
    create_backends,
    create_colbert_backend,
    create_dense_backend,
    create_sparse_backend,
)
from codebase_indexer.indexer.backends.colbert_remote import ColbertRemoteBackend
from codebase_indexer.indexer.backends.ollama_dense import OllamaDenseBackend
from codebase_indexer.indexer.backends.onnx_sparse import OnnxSparseBackend


def test_factory_uses_ollama_dense():
    settings = Settings(ollama_embed_model="nomic-embed-text")
    assert settings.dense_embed_backend == "ollama"
    dense = create_dense_backend(settings)
    sparse = create_sparse_backend(settings)
    assert isinstance(dense, OllamaDenseBackend)
    assert isinstance(sparse, OnnxSparseBackend)


def test_create_backends_returns_dense_and_sparse():
    settings = Settings(ollama_embed_model="nomic-embed-text")
    dense, sparse = create_backends(settings)
    assert isinstance(dense, OllamaDenseBackend)
    assert isinstance(sparse, OnnxSparseBackend)


def test_factory_uses_remote_colbert_by_default_when_rerank_on():
    settings = Settings(
        ollama_embed_model="nomic-embed-text",
        rerank_enabled=True,
    )
    assert settings.colbert_embed_backend == "remote"
    backend = create_colbert_backend(settings)
    assert isinstance(backend, ColbertRemoteBackend)
    assert backend.colbert_url == "http://colbert_worker:8082"


def test_factory_uses_remote_colbert_when_configured():
    settings = Settings(
        ollama_embed_model="nomic-embed-text",
        colbert_embed_backend="remote",
        colbert_url="http://colbert_worker:8082",
    )
    backend = create_colbert_backend(settings)
    assert isinstance(backend, ColbertRemoteBackend)
    assert backend.colbert_url == "http://colbert_worker:8082"

