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
    assert s.dense_embed_model == "jinaai/jina-embeddings-v2-base-code"
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


def test_tei_url_defaults():
    assert Settings().tei_url == "http://tei:80"


def test_tei_embed_batch_size_defaults():
    assert Settings().tei_embed_batch_size == 32


def test_tei_timeout_defaults():
    assert Settings().tei_timeout == 120


def test_tei_embed_dimensions_mrl():
    from codebase_indexer.config import tei_embed_dimensions

    assert tei_embed_dimensions("Qwen/Qwen3-Embedding-4B", 1024) == 1024
    assert tei_embed_dimensions("Qwen/Qwen3-Embedding-4B", 2560) is None
    assert tei_embed_dimensions("nomic-ai/nomic-embed-text-v1.5", 768) is None
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


def test_qwen3_specs_in_registry():
    from codebase_indexer.config import (
        KNOWN_EMBED_MODEL_DIMENSIONS,
        KNOWN_EMBED_MODEL_MAX_TOKENS,
        QWEN3_EMBED_SPECS,
    )

    assert QWEN3_EMBED_SPECS["Qwen/Qwen3-Embedding-4B"] == (2560, 32768)
    assert QWEN3_EMBED_SPECS["Qwen/Qwen3-Embedding-0.6B"] == (1024, 32768)
    assert QWEN3_EMBED_SPECS["Qwen/Qwen3-Embedding-8B"] == (4096, 32768)
    for model, (dim, max_tokens) in QWEN3_EMBED_SPECS.items():
        assert KNOWN_EMBED_MODEL_DIMENSIONS[model] == dim
        assert KNOWN_EMBED_MODEL_MAX_TOKENS[model] == max_tokens


def test_qwen3_4b_mrl_1024_valid():
    s = Settings(
        dense_embed_model="Qwen/Qwen3-Embedding-4B",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=1024,
        sparse_threads=2,
    )
    assert s.dense_embed_vector_size == 1024


def test_qwen3_mrl_rejects_below_minimum():
    with pytest.raises(ValueError, match="MRL"):
        Settings(
            dense_embed_model="Qwen/Qwen3-Embedding-4B",
            sparse_embed_model="Qdrant/bm25",
            dense_embed_vector_size=16,
            sparse_threads=2,
        )


def test_qwen3_mrl_rejects_above_native():
    with pytest.raises(ValueError, match="MRL"):
        Settings(
            dense_embed_model="Qwen/Qwen3-Embedding-4B",
            sparse_embed_model="Qdrant/bm25",
            dense_embed_vector_size=4096,
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
    assert s.rerank_adaptive_enabled is False


def test_rerank_adaptive_defaults_when_rerank_enabled():
    s = Settings(
        dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=768,
        sparse_threads=2,
        rerank_enabled=True,
    )
    assert s.rerank_adaptive_enabled is True
    assert s.rerank_adaptive_gap == 0.02


def test_rerank_adaptive_settings_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("RERANK_ENABLED", "true")
    monkeypatch.setenv("RERANK_ADAPTIVE_ENABLED", "false")
    monkeypatch.setenv("RERANK_ADAPTIVE_GAP", "0.05")
    s = Settings()
    assert s.rerank_adaptive_enabled is False
    assert s.rerank_adaptive_gap == 0.05


def test_rerank_adaptive_gap_rejects_negative():
    with pytest.raises(ValidationError):
        Settings(
            dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
            sparse_embed_model="Qdrant/bm25",
            dense_embed_vector_size=768,
            sparse_threads=2,
            rerank_enabled=True,
            rerank_adaptive_gap=-0.01,
        )


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


def test_rerank_auto_clamps_default_upsert_batch():
    s = Settings(
        dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=768,
        sparse_threads=2,
        rerank_enabled=True,
    )
    assert s.upsert_batch == 10


def test_rerank_rejects_explicit_large_upsert_batch():
    with pytest.raises(ValueError, match="UPSERT_BATCH"):
        Settings(
            dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
            sparse_embed_model="Qdrant/bm25",
            dense_embed_vector_size=768,
            sparse_threads=2,
            rerank_enabled=True,
            upsert_batch=500,
        )


def test_rerank_allows_explicit_small_upsert_batch():
    s = Settings(
        dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=768,
        sparse_threads=2,
        rerank_enabled=True,
        upsert_batch=16,
    )
    assert s.upsert_batch == 16


def test_colbert_specs_in_registry():
    from codebase_indexer.config import (
        COLBERT_EMBED_SPECS,
        KNOWN_COLBERT_MODEL_MAX_TOKENS,
        KNOWN_COLBERT_TOKEN_DIMENSIONS,
    )

    assert COLBERT_EMBED_SPECS["colbert-ir/colbertv2.0"] == (128, 512)
    assert KNOWN_COLBERT_TOKEN_DIMENSIONS["colbert-ir/colbertv2.0"] == 128
    assert KNOWN_COLBERT_MODEL_MAX_TOKENS["colbert-ir/colbertv2.0"] == 512


def test_colbert_embed_backend_defaults_to_onnx_when_rerank_off():
    assert Settings().colbert_embed_backend == "onnx"


def test_colbert_embed_backend_defaults_to_remote_when_rerank_on():
    s = Settings(
        dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=768,
        sparse_threads=2,
        rerank_enabled=True,
    )
    assert s.colbert_embed_backend == "remote"


def test_colbert_embed_backend_explicit_onnx_when_rerank_on(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("RERANK_ENABLED", "true")
    monkeypatch.setenv("COLBERT_EMBED_BACKEND", "onnx")
    assert Settings().colbert_embed_backend == "onnx"


def test_colbert_embed_backend_rejects_invalid():
    with pytest.raises(ValidationError):
        Settings(colbert_embed_backend="gpu")


def test_colbert_remote_requires_url_when_rerank_enabled():
    with pytest.raises(ValueError, match="COLBERT_URL"):
        Settings(
            dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
            sparse_embed_model="Qdrant/bm25",
            dense_embed_vector_size=768,
            sparse_threads=2,
            rerank_enabled=True,
            colbert_embed_backend="remote",
            colbert_url="",
        )


def test_colbert_remote_settings_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("COLBERT_EMBED_BACKEND", "remote")
    monkeypatch.setenv("COLBERT_URL", "http://colbert:8082")
    monkeypatch.setenv("COLBERT_TIMEOUT", "600")
    monkeypatch.setenv("COLBERT_EMBED_BATCH_SIZE", "8")
    s = Settings()
    assert s.colbert_embed_backend == "remote"
    assert s.colbert_url == "http://colbert:8082"
    assert s.colbert_timeout == 600
    assert s.colbert_embed_batch_size == 8


def test_recommend_defaults_enabled():
    s = Settings()
    assert s.recommend_enabled is True
    assert s.recommend_max_examples == 10


def test_recommend_settings_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("RECOMMEND_ENABLED", "false")
    monkeypatch.setenv("RECOMMEND_MAX_EXAMPLES", "5")
    s = Settings()
    assert s.recommend_enabled is False
    assert s.recommend_max_examples == 5


def test_outlier_defaults():
    s = Settings()
    assert s.outlier_max_context_samples == 200
    assert s.outlier_max_similarity == 0.55


def test_outlier_settings_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("OUTLIER_MAX_CONTEXT_SAMPLES", "50")
    monkeypatch.setenv("OUTLIER_MAX_SIMILARITY", "0.4")
    s = Settings()
    assert s.outlier_max_context_samples == 50
    assert s.outlier_max_similarity == 0.4


def test_graph_defaults_disabled():
    s = Settings()
    assert s.graph_enabled is False
    assert s.neo4j_uri == "bolt://neo4j:7687"
    assert s.neo4j_user == "neo4j"
    assert s.neo4j_password == ""
    assert s.neo4j_database == "neo4j"
    assert s.graph_writer_batch == 500
    assert s.graph_max_hops == 2
    assert s.graph_max_nodes == 200


def test_graph_settings_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("GRAPH_ENABLED", "true")
    monkeypatch.setenv("NEO4J_URI", "bolt://localhost:7687")
    monkeypatch.setenv("NEO4J_USER", "admin")
    monkeypatch.setenv("NEO4J_PASSWORD", "secret")
    monkeypatch.setenv("NEO4J_DATABASE", "codegraph")
    monkeypatch.setenv("GRAPH_WRITER_BATCH", "250")
    s = Settings()
    assert s.graph_enabled is True
    assert s.neo4j_uri == "bolt://localhost:7687"
    assert s.neo4j_user == "admin"
    assert s.neo4j_password == "secret"
    assert s.neo4j_database == "codegraph"
    assert s.graph_writer_batch == 250


def test_graph_enabled_requires_password():
    with pytest.raises(ValueError, match="NEO4J_PASSWORD"):
        Settings(
            dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
            sparse_embed_model="Qdrant/bm25",
            dense_embed_vector_size=768,
            sparse_threads=2,
            graph_enabled=True,
            neo4j_password="",
        )


def test_metrics_enabled_defaults_false():
    assert Settings().metrics_enabled is False


def test_metrics_enabled_from_env(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("METRICS_ENABLED", "true")
    assert Settings().metrics_enabled is True

