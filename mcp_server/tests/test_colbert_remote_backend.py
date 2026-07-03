"""Unit tests for ColbertRemoteBackend (mocked httpx)."""

from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from codebase_indexer.indexer.backends.base import EmbeddingError
from codebase_indexer.indexer.backends.colbert_remote import ColbertRemoteBackend


def _backend(**kwargs) -> ColbertRemoteBackend:
    defaults = dict(
        model_name="colbert-ir/colbertv2.0",
        colbert_url="http://localhost:8082",
        batch_size=2,
        timeout=30.0,
    )
    defaults.update(kwargs)
    return ColbertRemoteBackend(**defaults)


def test_preload_validates_token_dimension():
    backend = _backend()

    health_resp = MagicMock()
    health_resp.raise_for_status = MagicMock()
    health_resp.json.return_value = {
        "model": "colbert-ir/colbertv2.0",
        "token_dimension": 128,
        "loaded": True,
    }

    embed_resp = MagicMock()
    embed_resp.raise_for_status = MagicMock()
    embed_resp.json.return_value = {
        "embeddings": [[[0.1] * 128]],
        "token_dimension": 128,
    }

    mock_client = MagicMock()
    mock_client.get.return_value = health_resp
    mock_client.post.return_value = embed_resp
    mock_client.__enter__ = MagicMock(return_value=mock_client)
    mock_client.__exit__ = MagicMock(return_value=False)

    with patch("httpx.Client", return_value=mock_client):
        backend.preload()

    assert backend.is_loaded()


def test_preload_dimension_mismatch_raises():
    backend = _backend()

    health_resp = MagicMock()
    health_resp.raise_for_status = MagicMock()
    health_resp.json.return_value = {
        "model": "colbert-ir/colbertv2.0",
        "token_dimension": 128,
        "loaded": False,
    }

    embed_resp = MagicMock()
    embed_resp.raise_for_status = MagicMock()
    embed_resp.json.return_value = {
        "embeddings": [[[0.1] * 64]],
        "token_dimension": 128,
    }

    mock_client = MagicMock()
    mock_client.get.return_value = health_resp
    mock_client.post.return_value = embed_resp
    mock_client.__enter__ = MagicMock(return_value=mock_client)
    mock_client.__exit__ = MagicMock(return_value=False)

    with patch("httpx.Client", return_value=mock_client):
        with pytest.raises(EmbeddingError, match="dimension"):
            backend.preload()


@pytest.mark.asyncio
async def test_embed_batch_batches_requests():
    backend = _backend(batch_size=2)
    backend._ready = True

    responses = [
        {
            "embeddings": [
                [[1.0] * 128, [2.0] * 128],
                [[3.0] * 128],
            ],
            "token_dimension": 128,
        },
        {
            "embeddings": [[[4.0] * 128]],
            "token_dimension": 128,
        },
    ]
    call_count = 0

    async def fake_post(url, json=None):
        nonlocal call_count
        resp = MagicMock()
        resp.status_code = 200
        resp.raise_for_status = MagicMock()
        resp.json = MagicMock(return_value=responses[call_count])
        call_count += 1
        return resp

    mock_client = MagicMock()
    mock_client.post = AsyncMock(side_effect=fake_post)
    backend._async_client = mock_client

    result = await backend.embed_batch(["a", "b", "c"])
    assert len(result) == 3
    assert call_count == 2


@pytest.mark.asyncio
async def test_embed_batch_retries_on_503():
    backend = _backend(batch_size=10)
    backend._ready = True

    fail_resp = MagicMock()
    fail_resp.status_code = 503

    ok_resp = MagicMock()
    ok_resp.status_code = 200
    ok_resp.raise_for_status = MagicMock()
    ok_resp.json.return_value = {
        "embeddings": [[[1.0] * 128]],
        "token_dimension": 128,
    }

    mock_client = MagicMock()
    mock_client.post = AsyncMock(side_effect=[fail_resp, ok_resp])
    backend._async_client = mock_client

    with patch("asyncio.sleep", new_callable=AsyncMock):
        result = await backend.embed_batch(["hello"])
    assert result == [[[1.0] * 128]]
