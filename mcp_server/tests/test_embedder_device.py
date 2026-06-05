"""Unit tests for ONNX embed device / provider selection."""

import logging
import sys
from types import SimpleNamespace

from codebase_indexer.config import Settings
from codebase_indexer.context import AppContext
from codebase_indexer.indexer.embedder import Embedder


def _embedder(**overrides) -> Embedder:
    defaults = dict(
        dense_model="BAAI/bge-small-en-v1.5",
        sparse_model="Qdrant/bm25",
        dense_embed_vector_size=384,
        batch_size=128,
    )
    defaults.update(overrides)
    return Embedder(**defaults)


def test_embedder_default_providers_cpu():
    e = _embedder()
    assert e._providers == ["CPUExecutionProvider"]


def test_embedder_cuda_providers():
    e = _embedder(embed_device="cuda")
    assert e._providers == ["CUDAExecutionProvider", "CPUExecutionProvider"]


def test_embedder_rocm_providers():
    e = _embedder(embed_device="rocm")
    assert e._providers == [
        "MIGraphXExecutionProvider",
        "ROCMExecutionProvider",
        "CPUExecutionProvider",
    ]


def test_context_passes_embed_device():
    ctx = AppContext.create(Settings(embed_device="cuda"))
    assert ctx.embedder.embed_device == "cuda"


def test_dense_model_cache_separates_devices(monkeypatch):
    calls = []

    class FakeSession:
        def __init__(self, providers):
            self._providers = providers

        def get_providers(self):
            return self._providers

    class FakeInner:
        def __init__(self, providers):
            self.model = FakeSession(providers)

    class FakeTextEmbedding:
        def __init__(self, model_name, threads, providers):
            calls.append(
                {
                    "model_name": model_name,
                    "threads": threads,
                    "providers": providers,
                }
            )
            self.model = FakeInner(providers)

    monkeypatch.setitem(
        sys.modules,
        "fastembed",
        SimpleNamespace(TextEmbedding=FakeTextEmbedding),
    )

    try:
        Embedder.release_models()
        _embedder(embed_device="cpu")._get_dense_model()
        _embedder(embed_device="cuda")._get_dense_model()
    finally:
        Embedder.release_models()

    assert [call["providers"] for call in calls] == [
        ["CPUExecutionProvider"],
        ["CUDAExecutionProvider", "CPUExecutionProvider"],
    ]


def test_dense_model_cache_separates_rocm_from_cpu_cuda(monkeypatch):
    calls = []

    class FakeSession:
        def __init__(self, providers):
            self._providers = providers

        def get_providers(self):
            return self._providers

    class FakeInner:
        def __init__(self, providers):
            self.model = FakeSession(providers)

    class FakeTextEmbedding:
        def __init__(self, model_name, threads, providers):
            calls.append({"providers": providers})
            self.model = FakeInner(providers)

    monkeypatch.setitem(
        sys.modules,
        "fastembed",
        SimpleNamespace(TextEmbedding=FakeTextEmbedding),
    )

    try:
        Embedder.release_models()
        cpu = _embedder(embed_device="cpu")
        cuda = _embedder(embed_device="cuda")
        rocm = _embedder(embed_device="rocm")
        cpu._get_dense_model()
        cuda._get_dense_model()
        rocm._get_dense_model()
    finally:
        Embedder.release_models()

    provider_lists = [call["providers"] for call in calls]
    assert provider_lists[0] == ["CPUExecutionProvider"]
    assert provider_lists[1] == ["CUDAExecutionProvider", "CPUExecutionProvider"]
    assert provider_lists[2] == [
        "MIGraphXExecutionProvider",
        "ROCMExecutionProvider",
        "CPUExecutionProvider",
    ]
    assert provider_lists[0] != provider_lists[2]
    assert provider_lists[1] != provider_lists[2]


def test_log_dense_providers_warns_when_cuda_unavailable(caplog):
    e = _embedder(embed_device="cuda")

    class FakeSession:
        def get_providers(self):
            return ["CPUExecutionProvider"]

    class FakeInner:
        model = FakeSession()

    class FakeModel:
        model = FakeInner()

    with caplog.at_level(logging.WARNING, logger="codebase_indexer.indexer.embedder"):
        active = e._log_dense_providers(FakeModel())

    assert active == ["CPUExecutionProvider"]
    assert "cuda_requested_but_unavailable" in caplog.text


def test_log_dense_providers_warns_when_rocm_unavailable(caplog):
    e = _embedder(embed_device="rocm")

    class FakeSession:
        def get_providers(self):
            return ["CPUExecutionProvider"]

    class FakeInner:
        model = FakeSession()

    class FakeModel:
        model = FakeInner()

    with caplog.at_level(logging.WARNING, logger="codebase_indexer.indexer.embedder"):
        active = e._log_dense_providers(FakeModel())

    assert active == ["CPUExecutionProvider"]
    assert "rocm_requested_but_unavailable" in caplog.text
