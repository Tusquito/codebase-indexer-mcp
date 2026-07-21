"""Shared pytest fixtures."""

import pytest

import codebase_indexer.telemetry.metrics as metrics_mod

# Values mirror .env.example (single place for deployment defaults).
_TEST_DENSE_EMBED_MODEL = "jinaai/jina-embeddings-v2-base-code"
_TEST_SPARSE_EMBED_MODEL = "Qdrant/bm25"
_TEST_DENSE_EMBED_VECTOR_SIZE = "768"
_TEST_SPARSE_THREADS = "2"


@pytest.fixture(autouse=True)
def _reset_telemetry_metrics_state() -> None:
    """Isolate prometheus registry between tests."""
    metrics_mod._initialized = False
    metrics_mod._enabled = False
    metrics_mod._registry = None
    metrics_mod._mcp_tool_requests = None
    metrics_mod._mcp_tool_duration = None
    metrics_mod._search_results = None
    metrics_mod._index_jobs = None
    metrics_mod._index_duration = None
    metrics_mod._index_chunks = None
    metrics_mod._embed_requests = None
    metrics_mod._memory_pressure = None
    metrics_mod._truncated_chunks = None
    yield


@pytest.fixture(autouse=True)
def _required_embed_env(monkeypatch: pytest.MonkeyPatch) -> None:
    """Pin Settings defaults so host ``.env`` / shell opt-ins cannot pollute tests.

    Operator machines often export ``GRAPH_ENABLED`` / ``RERANK_ENABLED`` for the
    live Docker stack; without isolation, ``Settings()`` default assertions and
    storage-integration constructors fail spuriously.
    """
    monkeypatch.setenv("DENSE_EMBED_MODEL", _TEST_DENSE_EMBED_MODEL)
    monkeypatch.setenv("SPARSE_EMBED_MODEL", _TEST_SPARSE_EMBED_MODEL)
    monkeypatch.setenv("DENSE_EMBED_VECTOR_SIZE", _TEST_DENSE_EMBED_VECTOR_SIZE)
    monkeypatch.setenv("SPARSE_THREADS", _TEST_SPARSE_THREADS)
    # Opt-in features — force production defaults regardless of host env /.env
    monkeypatch.setenv("HYBRID_SEARCH", "true")
    monkeypatch.setenv("RERANK_ENABLED", "false")
    monkeypatch.setenv("GRAPH_ENABLED", "false")
    monkeypatch.setenv("NEO4J_PASSWORD", "")
    monkeypatch.setenv("METRICS_ENABLED", "false")
    monkeypatch.setenv("RECOMMEND_ENABLED", "true")
    monkeypatch.setenv("MCP_AUTH_TOKEN", "")
    # Leave COLBERT_EMBED_BACKEND unset so Settings can default onnx→remote when
    # individual tests enable RERANK_ENABLED (host .env must not pin "onnx").
    monkeypatch.delenv("COLBERT_EMBED_BACKEND", raising=False)
    monkeypatch.delenv("UPSERT_BATCH", raising=False)
    monkeypatch.setenv("COLBERT_URL", "http://colbert_worker:8082")
