"""Unit tests for Ollama dense embedding backend."""

from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from codebase_indexer.indexer.backends.base import EmbeddingError
from codebase_indexer.indexer.backends.ollama_dense import OllamaDenseBackend


def _backend(**kwargs) -> OllamaDenseBackend:
    defaults = dict(
        model_name="nomic-embed-text",
        vector_size=3,
        ollama_url="http://localhost:11434",
        batch_size=2,
    )
    defaults.update(kwargs)
    return OllamaDenseBackend(**defaults)


@pytest.fixture(autouse=True)
def _reset_tokenizer_state():
    OllamaDenseBackend._shared_tokenizer = None
    OllamaDenseBackend._tokenizer_load_attempted = False
    yield
    OllamaDenseBackend._shared_tokenizer = None
    OllamaDenseBackend._tokenizer_load_attempted = False


class _MockEncoding:
    def __init__(self, ids, offsets):
        self.ids = ids
        self.offsets = offsets


class _MockTokenizer:
    """Minimal HF tokenizers stand-in with char offsets per whitespace token."""

    def encode(self, text, add_special_tokens=False):
        parts = text.split()
        offsets = []
        pos = 0
        for part in parts:
            start = text.find(part, pos)
            end = start + len(part)
            offsets.append((start, end))
            pos = end + 1
        return _MockEncoding(ids=list(range(len(parts))), offsets=offsets)

    def decode(self, ids):
        return " ".join(f"tok{i}" for i in ids)


def test_preload_validates_dimension(monkeypatch):
    backend = _backend(vector_size=768)

    class FakeResponse:
        def raise_for_status(self):
            return None

        def json(self):
            return {"models": [{"name": "nomic-embed-text:latest"}]}

    embed_resp = MagicMock()
    embed_resp.raise_for_status = MagicMock()
    embed_resp.json.return_value = {"embeddings": [[0.1] * 768]}

    mock_client = MagicMock()
    mock_client.get.return_value = FakeResponse()
    mock_client.post.return_value = embed_resp
    mock_client.__enter__ = MagicMock(return_value=mock_client)
    mock_client.__exit__ = MagicMock(return_value=False)

    with patch(
        "codebase_indexer.indexer.backends.ollama_dense.load_dense_tokenizer",
        return_value=None,
    ):
        with patch(
            "codebase_indexer.indexer.backends.ollama_dense.httpx.Client",
            return_value=mock_client,
        ):
            backend.preload()

    assert backend.is_loaded()


def test_preload_dimension_mismatch_raises(monkeypatch):
    backend = _backend(vector_size=768)

    class FakeResponse:
        def raise_for_status(self):
            return None

        def json(self):
            return {"models": []}

    embed_resp = MagicMock()
    embed_resp.raise_for_status = MagicMock()
    embed_resp.json.return_value = {"embeddings": [[0.1] * 384]}

    mock_client = MagicMock()
    mock_client.get.return_value = FakeResponse()
    mock_client.post.return_value = embed_resp
    mock_client.__enter__ = MagicMock(return_value=mock_client)
    mock_client.__exit__ = MagicMock(return_value=False)

    with patch(
        "codebase_indexer.indexer.backends.ollama_dense.load_dense_tokenizer",
        return_value=None,
    ):
        with patch(
            "codebase_indexer.indexer.backends.ollama_dense.httpx.Client",
            return_value=mock_client,
        ):
            with pytest.raises(EmbeddingError, match="dimension"):
                backend.preload()


@pytest.mark.asyncio
async def test_embed_batch_truncates_when_max_tokens_set():
    backend = _backend(
        vector_size=2,
        batch_size=10,
        max_dense_embed_tokens=3,
        dense_embed_model="jinaai/jina-embeddings-v2-base-code",
    )
    backend._ready = True
    OllamaDenseBackend._shared_tokenizer = _MockTokenizer()
    OllamaDenseBackend._tokenizer_load_attempted = True

    captured_inputs: list[list[str]] = []

    async def fake_post(url, json=None):
        captured_inputs.append(json["input"])
        resp = MagicMock()
        resp.status_code = 200
        resp.raise_for_status = MagicMock()
        resp.json = MagicMock(
            return_value={"embeddings": [[1.0, 2.0] for _ in json["input"]]}
        )
        return resp

    mock_client = MagicMock()
    mock_client.post = AsyncMock(side_effect=fake_post)
    backend._async_client = mock_client

    long_text = "alpha beta gamma delta epsilon zeta"
    await backend.embed_batch([long_text])
    assert captured_inputs
    assert captured_inputs[0][0] == "alpha beta gamma"


@pytest.mark.asyncio
async def test_embed_batch_passes_through_when_tokenizer_unavailable():
    backend = _backend(
        vector_size=2,
        batch_size=10,
        max_dense_embed_tokens=3,
        dense_embed_model="jinaai/jina-embeddings-v2-base-code",
    )
    backend._ready = True
    OllamaDenseBackend._shared_tokenizer = None
    OllamaDenseBackend._tokenizer_load_attempted = True

    captured_inputs: list[list[str]] = []

    async def fake_post(url, json=None):
        captured_inputs.append(json["input"])
        resp = MagicMock()
        resp.status_code = 200
        resp.raise_for_status = MagicMock()
        resp.json = MagicMock(
            return_value={"embeddings": [[1.0, 2.0] for _ in json["input"]]}
        )
        return resp

    mock_client = MagicMock()
    mock_client.post = AsyncMock(side_effect=fake_post)
    backend._async_client = mock_client

    long_text = "alpha beta gamma delta epsilon zeta"
    await backend.embed_batch([long_text])
    assert captured_inputs
    assert captured_inputs[0][0] == long_text


def test_preload_loads_dense_tokenizer():
    backend = _backend(vector_size=768, dense_embed_model="nomic-ai/nomic-embed-text-v1.5")
    mock_tok = _MockTokenizer()

    class FakeResponse:
        def raise_for_status(self):
            return None

        def json(self):
            return {"models": [{"name": "nomic-embed-text:latest"}]}

    embed_resp = MagicMock()
    embed_resp.raise_for_status = MagicMock()
    embed_resp.json.return_value = {"embeddings": [[0.1] * 768]}

    mock_client = MagicMock()
    mock_client.get.return_value = FakeResponse()
    mock_client.post.return_value = embed_resp
    mock_client.__enter__ = MagicMock(return_value=mock_client)
    mock_client.__exit__ = MagicMock(return_value=False)

    with patch("codebase_indexer.indexer.backends.ollama_dense.httpx.Client", return_value=mock_client):
        with patch(
            "codebase_indexer.indexer.backends.ollama_dense.load_dense_tokenizer",
            return_value=mock_tok,
        ) as load_mock:
            backend.preload()

    load_mock.assert_called_once_with("nomic-ai/nomic-embed-text-v1.5")
    assert OllamaDenseBackend._shared_tokenizer is mock_tok


@pytest.mark.asyncio
async def test_embed_batch_batches_requests():
    backend = _backend(vector_size=2, batch_size=2)
    backend._ready = True

    responses = [
        {"embeddings": [[1.0, 2.0], [3.0, 4.0]]},
        {"embeddings": [[5.0, 6.0]]},
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
    backend = _backend(vector_size=2, batch_size=10)
    backend._ready = True

    fail_resp = MagicMock()
    fail_resp.status_code = 503

    ok_resp = MagicMock()
    ok_resp.status_code = 200
    ok_resp.raise_for_status = MagicMock()
    ok_resp.json.return_value = {"embeddings": [[1.0, 2.0]]}

    mock_client = MagicMock()
    mock_client.post = AsyncMock(side_effect=[fail_resp, ok_resp])
    backend._async_client = mock_client

    with patch("asyncio.sleep", new_callable=AsyncMock):
        result = await backend.embed_batch(["hello"])
    assert result == [[1.0, 2.0]]
