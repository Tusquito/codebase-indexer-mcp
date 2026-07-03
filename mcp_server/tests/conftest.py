"""Shared pytest fixtures."""

import pytest

import codebase_indexer.telemetry.metrics as metrics_mod

# Values mirror .env.example (single place for deployment defaults).
_TEST_DENSE_EMBED_MODEL = "Qwen/Qwen3-Embedding-4B"
_TEST_SPARSE_EMBED_MODEL = "Qdrant/bm25"
_TEST_DENSE_EMBED_VECTOR_SIZE = "1024"
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
    """Tests construct Settings() without repeating embed env vars."""
    monkeypatch.setenv("DENSE_EMBED_MODEL", _TEST_DENSE_EMBED_MODEL)
    monkeypatch.setenv("SPARSE_EMBED_MODEL", _TEST_SPARSE_EMBED_MODEL)
    monkeypatch.setenv("DENSE_EMBED_VECTOR_SIZE", _TEST_DENSE_EMBED_VECTOR_SIZE)
    monkeypatch.setenv("SPARSE_THREADS", _TEST_SPARSE_THREADS)
