"""Unit tests for Settings parsing helpers."""

import pytest
from pydantic import ValidationError

from codebase_indexer.config import Settings


def test_service_url_keyword_list_parses_csv():
    s = Settings(service_url_keywords="a, b ,c,")
    assert s.service_url_keyword_list == ["a", "b", "c"]


def test_service_discovery_extra_query_list_handles_pipe_and_newlines():
    s = Settings(service_discovery_extra_queries="q1|q2\n q3 ")
    assert s.service_discovery_extra_query_list == ["q1", "q2", "q3"]


def test_service_discovery_extra_query_list_empty_by_default():
    assert Settings().service_discovery_extra_query_list == []


def test_auth_token_defaults_empty():
    assert Settings().mcp_auth_token == ""


def test_embed_settings_loaded_from_env():
    s = Settings()
    assert s.dense_embed_model == "nomic-ai/nomic-embed-text-v1.5"
    assert s.sparse_embed_model == "Qdrant/bm25"
    assert s.dense_embed_vector_size == 768
    assert s.sparse_threads == 2


def test_embed_settings_required(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.delenv("DENSE_EMBED_MODEL", raising=False)
    monkeypatch.delenv("SPARSE_EMBED_MODEL", raising=False)
    monkeypatch.delenv("DENSE_EMBED_VECTOR_SIZE", raising=False)
    monkeypatch.delenv("SPARSE_THREADS", raising=False)
    with pytest.raises(ValidationError):
        Settings()


def test_sparse_threads_required(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.delenv("SPARSE_THREADS", raising=False)
    with pytest.raises(ValidationError):
        Settings()


def test_custom_model_with_explicit_dense_embed_vector_size_valid():
    s = Settings(
        dense_embed_model="custom/model",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=384,
        sparse_threads=2,
    )
    assert s.dense_embed_vector_size == 384


def test_known_model_wrong_dense_embed_vector_size_rejected():
    with pytest.raises(ValueError, match="does not match"):
        Settings(
            dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
            sparse_embed_model="Qdrant/bm25",
            dense_embed_vector_size=384,
            sparse_threads=2,
        )


def test_bge_base_model_dense_embed_vector_size_valid():
    s = Settings(
        dense_embed_model="BAAI/bge-base-en-v1.5",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=768,
        sparse_threads=2,
    )
    assert s.dense_embed_model == "BAAI/bge-base-en-v1.5"


def test_bge_small_model_dense_embed_vector_size_valid():
    s = Settings(
        dense_embed_model="BAAI/bge-small-en-v1.5",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=384,
        sparse_threads=2,
    )
    assert s.dense_embed_vector_size == 384
