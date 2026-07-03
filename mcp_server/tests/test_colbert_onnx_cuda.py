"""Unit tests for ColbertOnnxBackend CUDA wiring (mocked fastembed)."""

from unittest.mock import MagicMock, patch

import pytest

from codebase_indexer.indexer.backends.colbert_onnx import ColbertOnnxBackend


@pytest.fixture(autouse=True)
def _reset_shared_model():
    ColbertOnnxBackend.release_shared()
    yield
    ColbertOnnxBackend.release_shared()


def test_colbert_onnx_passes_cuda_and_device_ids_to_fastembed():
    mock_model = MagicMock()

    with patch(
        "fastembed.late_interaction.LateInteractionTextEmbedding",
        return_value=mock_model,
    ) as mock_cls:
        backend = ColbertOnnxBackend(
            model_name="colbert-ir/colbertv2.0",
            sparse_threads=2,
            use_cuda=True,
            device_ids=[0, 1],
        )
        backend.preload()

    mock_cls.assert_called_once_with(
        model_name="colbert-ir/colbertv2.0",
        threads=2,
        cuda=True,
        device_ids=[0, 1],
    )


def test_colbert_onnx_defaults_cpu_without_device_ids():
    mock_model = MagicMock()

    with patch(
        "fastembed.late_interaction.LateInteractionTextEmbedding",
        return_value=mock_model,
    ) as mock_cls:
        backend = ColbertOnnxBackend(
            model_name="colbert-ir/colbertv2.0",
            sparse_threads=2,
        )
        backend.preload()

    mock_cls.assert_called_once_with(
        model_name="colbert-ir/colbertv2.0",
        threads=2,
        cuda=False,
        device_ids=None,
    )
