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


def test_max_sparse_embed_tokens_defaults_to_zero():
    assert Settings().max_sparse_embed_tokens == 0


def test_max_sparse_embed_tokens_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("MAX_SPARSE_EMBED_TOKENS", "512")
    assert Settings().max_sparse_embed_tokens == 512


def test_max_dense_embed_tokens_defaults_to_zero():
    assert Settings().max_dense_embed_tokens == 0


def test_max_dense_embed_tokens_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("MAX_DENSE_EMBED_TOKENS", "2048")
    assert Settings().max_dense_embed_tokens == 2048


def test_sequential_embed_defaults_false():
    assert Settings().sequential_embed is False


def test_sequential_embed_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("SEQUENTIAL_EMBED", "true")
    assert Settings().sequential_embed is True


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


def test_jina_code_model_dense_embed_vector_size_valid():
    s = Settings(
        dense_embed_model="jinaai/jina-embeddings-v2-base-code",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=768,
        sparse_threads=2,
    )
    assert s.dense_embed_model == "jinaai/jina-embeddings-v2-base-code"


def test_dense_embed_backend_defaults_to_ollama():
    assert Settings().dense_embed_backend == "ollama"


def test_dense_embed_backend_rejects_non_ollama():
    with pytest.raises(ValidationError):
        Settings(dense_embed_backend="onnx")


def test_bge_v15_official_specs_in_registry():
    from codebase_indexer.config import (
        BGE_EN_V1_5_SPECS,
        KNOWN_EMBED_MODEL_DIMENSIONS,
        KNOWN_EMBED_MODEL_MAX_TOKENS,
    )

    assert BGE_EN_V1_5_SPECS["BAAI/bge-base-en-v1.5"] == (768, 512)
    assert BGE_EN_V1_5_SPECS["BAAI/bge-small-en-v1.5"] == (384, 512)
    for model, (dim, max_tokens) in BGE_EN_V1_5_SPECS.items():
        assert KNOWN_EMBED_MODEL_DIMENSIONS[model] == dim
        assert KNOWN_EMBED_MODEL_MAX_TOKENS[model] == max_tokens


def test_jina_code_v2_specs_in_registry():
    from codebase_indexer.config import (
        JINA_CODE_EMBED_V2_SPECS,
        KNOWN_EMBED_MODEL_DIMENSIONS,
        KNOWN_EMBED_MODEL_MAX_TOKENS,
    )

    assert JINA_CODE_EMBED_V2_SPECS["jinaai/jina-embeddings-v2-base-code"] == (768, 8192)
    for model, (dim, max_tokens) in JINA_CODE_EMBED_V2_SPECS.items():
        assert KNOWN_EMBED_MODEL_DIMENSIONS[model] == dim
        assert KNOWN_EMBED_MODEL_MAX_TOKENS[model] == max_tokens


def test_jina_code_wrong_dense_embed_vector_size_rejected():
    with pytest.raises(ValueError, match="does not match"):
        Settings(
            dense_embed_model="jinaai/jina-embeddings-v2-base-code",
            sparse_embed_model="Qdrant/bm25",
            dense_embed_vector_size=384,
            sparse_threads=2,
        )


def test_preload_models_defaults_true():
    assert Settings().preload_models is True


def test_preload_models_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("PRELOAD_MODELS", "false")
    assert Settings().preload_models is False


def test_qdrant_search_tuning_defaults():
    s = Settings()
    assert s.quant_oversampling == 2.0
    assert s.hnsw_ef == 64
    assert s.hnsw_m == 16
    assert s.hnsw_ef_construct == 128
    assert s.prefetch_multiplier == 5
    assert s.rrf_k == 60


def test_qdrant_search_tuning_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("QUANT_OVERSAMPLING", "3.5")
    monkeypatch.setenv("HNSW_EF", "128")
    monkeypatch.setenv("HNSW_M", "32")
    monkeypatch.setenv("HNSW_EF_CONSTRUCT", "256")
    monkeypatch.setenv("PREFETCH_MULTIPLIER", "7")
    monkeypatch.setenv("RRF_K", "40")
    s = Settings()
    assert s.quant_oversampling == 3.5
    assert s.hnsw_ef == 128
    assert s.hnsw_m == 32
    assert s.hnsw_ef_construct == 256
    assert s.prefetch_multiplier == 7
    assert s.rrf_k == 40


def test_rerank_defaults_disabled():
    s = Settings()
    assert s.rerank_enabled is False
    assert s.colbert_embed_model == "colbert-ir/colbertv2.0"
    assert s.rerank_prefetch == 100
    assert s.rerank_max_query_tokens == 0


def test_rerank_settings_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("RERANK_ENABLED", "true")
    monkeypatch.setenv("COLBERT_EMBED_MODEL", "colbert-ir/colbertv2.0")
    monkeypatch.setenv("RERANK_PREFETCH", "50")
    monkeypatch.setenv("RERANK_MAX_QUERY_TOKENS", "256")
    s = Settings()
    assert s.rerank_enabled is True
    assert s.rerank_prefetch == 50
    assert s.rerank_max_query_tokens == 256


def test_rerank_requires_hybrid():
    with pytest.raises(ValueError, match="HYBRID_SEARCH"):
        Settings(
            dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
            sparse_embed_model="Qdrant/bm25",
            dense_embed_vector_size=768,
            sparse_threads=2,
            hybrid_search=False,
            rerank_enabled=True,
        )


def test_colbert_specs_in_registry():
    from codebase_indexer.config import (
        COLBERT_EMBED_SPECS,
        KNOWN_COLBERT_MODEL_MAX_TOKENS,
        KNOWN_COLBERT_TOKEN_DIMENSIONS,
    )

    assert COLBERT_EMBED_SPECS["colbert-ir/colbertv2.0"] == (128, 512)
    assert KNOWN_COLBERT_TOKEN_DIMENSIONS["colbert-ir/colbertv2.0"] == 128
    assert KNOWN_COLBERT_MODEL_MAX_TOKENS["colbert-ir/colbertv2.0"] == 512
