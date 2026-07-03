"""Unit tests for colbert_worker FastAPI app."""

from unittest.mock import AsyncMock, MagicMock

import pytest
from fastapi.testclient import TestClient

from codebase_indexer.colbert_worker.app import create_app
from codebase_indexer.colbert_worker.settings import WorkerSettings
from codebase_indexer.indexer.backends.colbert_onnx import ColbertOnnxBackend


@pytest.fixture
def mock_backend():
    backend = MagicMock(spec=ColbertOnnxBackend)
    backend.model_name = "colbert-ir/colbertv2.0"
    backend.token_dimension = 128
    backend.is_loaded.return_value = True
    backend.preload = MagicMock()
    backend.embed_batch = AsyncMock(
        return_value=[
            [[1.0, 0.0], [0.0, 1.0]],
            [[0.5, 0.5]],
        ]
    )
    return backend


@pytest.fixture
def client(mock_backend):
    settings = WorkerSettings(
        colbert_embed_model="colbert-ir/colbertv2.0",
        sparse_threads=2,
    )
    app = create_app(settings=settings, backend=mock_backend)
    with TestClient(app, raise_server_exceptions=False) as test_client:
        yield test_client


def test_health_returns_model_info(client, mock_backend):
    resp = client.get("/health")
    assert resp.status_code == 200
    data = resp.json()
    assert data["model"] == "colbert-ir/colbertv2.0"
    assert data["token_dimension"] == 128
    assert data["loaded"] is True


def test_embed_colbert_returns_multivectors(client, mock_backend):
    resp = client.post(
        "/v1/embed/colbert",
        json={"texts": ["hello", "world"]},
    )
    assert resp.status_code == 200
    data = resp.json()
    assert data["token_dimension"] == 128
    assert len(data["embeddings"]) == 2
    mock_backend.embed_batch.assert_called_once()


def test_embed_colbert_rejects_empty_texts(client):
    resp = client.post("/v1/embed/colbert", json={"texts": []})
    assert resp.status_code == 422
