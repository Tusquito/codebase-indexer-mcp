"""Unit tests for ColbertOnnxBackend (mocked fastembed)."""

from unittest.mock import MagicMock, patch

import numpy as np
import pytest

from codebase_indexer.indexer.backends.colbert_onnx import ColbertOnnxBackend


@pytest.fixture(autouse=True)
def _reset_shared_model():
    ColbertOnnxBackend.release_shared()
    yield
    ColbertOnnxBackend.release_shared()


@pytest.mark.asyncio
async def test_colbert_embed_batch_returns_multivectors():
    mock_model = MagicMock()
    mock_model.embed.return_value = iter(
        [
            np.array([[1.0, 0.0], [0.0, 1.0]], dtype=np.float32),
            np.array([[0.5, 0.5]], dtype=np.float32),
        ]
    )

    with patch(
        "fastembed.late_interaction.LateInteractionTextEmbedding",
        return_value=mock_model,
    ):
        backend = ColbertOnnxBackend(
            model_name="colbert-ir/colbertv2.0",
            sparse_threads=2,
            max_query_tokens=512,
        )
        result = await backend.embed_batch(["hello", "world"])

    assert len(result) == 2
    assert result[0] == [[1.0, 0.0], [0.0, 1.0]]
    assert result[1] == [[0.5, 0.5]]
    mock_model.embed.assert_called_once()


def test_colbert_token_dimension_from_registry():
    backend = ColbertOnnxBackend(model_name="colbert-ir/colbertv2.0", sparse_threads=2)
    assert backend.token_dimension == 128


def test_colbert_release_clears_shared_model():
    ColbertOnnxBackend._shared_model = object()
    backend = ColbertOnnxBackend(model_name="colbert-ir/colbertv2.0", sparse_threads=2)
    backend.release()
    assert ColbertOnnxBackend._shared_model is None
