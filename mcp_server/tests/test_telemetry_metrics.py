"""Unit tests for Prometheus application metrics (ADR 0018 Phase 1)."""

from __future__ import annotations

import pytest
from fastapi.testclient import TestClient

from codebase_indexer.colbert_worker.app import create_app
from codebase_indexer.colbert_worker.settings import WorkerSettings
from codebase_indexer.config import Settings
from codebase_indexer.indexer.backends.colbert_onnx import ColbertOnnxBackend
from codebase_indexer.main import create_app as create_mcp_app
from codebase_indexer.telemetry.metrics import (
    init_metrics,
    observe_tool,
    record_embed_request,
    record_index_job,
    record_memory_pressure,
    record_search_results,
    render_metrics,
)


def test_render_metrics_empty_when_disabled():
    init_metrics(False)
    body, _ = render_metrics()
    assert body == b""


def test_observe_tool_increments_counter_when_enabled():
    init_metrics(True)

    @observe_tool("test_tool")
    async def ok_tool() -> str:
        return "ok"

    import asyncio

    asyncio.run(ok_tool())
    body, _ = render_metrics()
    text = body.decode()
    assert "codeindexer_mcp_tool_requests_total" in text
    assert 'tool="test_tool"' in text
    assert 'status="success"' in text
    assert "codeindexer_mcp_tool_duration_seconds" in text


def test_observe_tool_records_error_status():
    init_metrics(True)

    @observe_tool("fail_tool")
    async def fail_tool() -> None:
        raise ValueError("boom")

    import asyncio

    with pytest.raises(ValueError, match="boom"):
        asyncio.run(fail_tool())
    body, _ = render_metrics()
    text = body.decode()
    assert 'tool="fail_tool"' in text
    assert 'status="error"' in text


def test_record_helpers_emit_expected_series():
    init_metrics(True)
    record_search_results(5, rerank=True)
    record_index_job("done", 12.5, 100)
    record_embed_request("tei", "success")
    record_memory_pressure("warn")
    body, _ = render_metrics()
    text = body.decode()
    assert "codeindexer_search_results" in text
    assert 'rerank="true"' in text
    assert "codeindexer_index_jobs_total" in text
    assert 'status="done"' in text
    assert "codeindexer_index_duration_seconds" in text
    assert "codeindexer_index_chunks_total" in text
    assert "codeindexer_embed_requests_total" in text
    assert 'backend="tei"' in text
    assert "codeindexer_memory_pressure_events_total" in text
    assert 'severity="warn"' in text


def test_mcp_metrics_route_404_when_disabled():
    settings = Settings(metrics_enabled=False, preload_models=False)
    with pytest.MonkeyPatch.context() as mp:
        mp.setattr("codebase_indexer.main.AppContext.create", lambda s: _mock_ctx(s))
        mcp = create_mcp_app(settings, preload_models=False)
    app = mcp.http_app()
    from starlette.testclient import TestClient as StarletteTestClient

    with StarletteTestClient(app) as client:
        resp = client.get("/metrics")
    assert resp.status_code == 404


def test_mcp_metrics_route_200_when_enabled():
    settings = Settings(metrics_enabled=True, preload_models=False)
    with pytest.MonkeyPatch.context() as mp:
        mp.setattr("codebase_indexer.main.AppContext.create", lambda s: _mock_ctx(s))
        mcp = create_mcp_app(settings, preload_models=False)
    app = mcp.http_app()
    from starlette.testclient import TestClient as StarletteTestClient

    with StarletteTestClient(app) as client:
        resp = client.get("/metrics")
    assert resp.status_code == 200
    assert "codeindexer_mcp_tool_requests_total" in resp.text


def _mock_ctx(settings: Settings):
    from unittest.mock import MagicMock

    ctx = MagicMock()
    ctx.settings = settings
    return ctx


@pytest.fixture
def mock_colbert_backend():
    from unittest.mock import AsyncMock, MagicMock

    backend = MagicMock(spec=ColbertOnnxBackend)
    backend.model_name = "colbert-ir/colbertv2.0"
    backend.token_dimension = 128
    backend.is_loaded.return_value = True
    backend.active_device.return_value = "cpu"
    backend.execution_providers.return_value = ["CPUExecutionProvider"]
    backend.preload = MagicMock()
    backend.embed_batch = AsyncMock(return_value=[[[1.0, 0.0]]])
    return backend


def test_colbert_metrics_disabled_returns_404(mock_colbert_backend):
    settings = WorkerSettings(metrics_enabled=False)
    app = create_app(settings=settings, backend=mock_colbert_backend)
    with TestClient(app, raise_server_exceptions=False) as client:
        resp = client.get("/metrics")
    assert resp.status_code == 404


def test_colbert_metrics_enabled_returns_prometheus(mock_colbert_backend):
    settings = WorkerSettings(metrics_enabled=True)
    app = create_app(settings=settings, backend=mock_colbert_backend)
    with TestClient(app, raise_server_exceptions=False) as client:
        resp = client.get("/metrics")
    assert resp.status_code == 200
    assert "codeindexer_embed_requests_total" in resp.text
