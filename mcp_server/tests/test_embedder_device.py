"""Unit tests for ONNX embed device / provider selection."""

import builtins
import logging
import sys
from types import SimpleNamespace

import pytest

from codebase_indexer.config import Settings
from codebase_indexer.context import AppContext
from codebase_indexer.indexer import embedder as embedder_mod
from codebase_indexer.indexer.embedder import (
    Embedder,
    available_onnx_providers,
    filter_available_providers,
    resolve_onnx_providers,
)

_ALL_ONNX_PROVIDERS = [
    "MIGraphXExecutionProvider",
    "ROCMExecutionProvider",
    "CUDAExecutionProvider",
    "CPUExecutionProvider",
]


@pytest.fixture(autouse=True)
def _default_all_providers_available(monkeypatch, request):
    """Preserve pre-filter behavior for legacy cache-key tests on CPU-only hosts."""
    skip_autouse = (
        "filter_available_providers" in request.node.name
        or "available_onnx_providers" in request.node.name
        or "get_dense_model_passes_cpu_only" in request.node.name
        or "onnx_providers_filtered" in request.node.name
        or "falls_back_to_cpu_when_gpu_init_fails" in request.node.name
    )
    if skip_autouse:
        return
    monkeypatch.setattr(
        embedder_mod,
        "available_onnx_providers",
        lambda: list(_ALL_ONNX_PROVIDERS),
    )


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


def test_filter_available_providers_drops_unavailable():
    desired = resolve_onnx_providers("rocm")
    available = ["ROCMExecutionProvider", "CPUExecutionProvider"]
    kept, dropped = filter_available_providers(desired, available)
    assert kept == ["ROCMExecutionProvider", "CPUExecutionProvider"]
    assert dropped == ["MIGraphXExecutionProvider"]


def test_filter_available_providers_preserves_priority_order():
    desired = [
        "MIGraphXExecutionProvider",
        "ROCMExecutionProvider",
        "CPUExecutionProvider",
    ]
    available = [
        "CPUExecutionProvider",
        "ROCMExecutionProvider",
        "MIGraphXExecutionProvider",
    ]
    kept, dropped = filter_available_providers(desired, available)
    assert kept == desired
    assert dropped == []


def test_filter_available_providers_always_retains_cpu():
    desired = ["CUDAExecutionProvider"]
    available = ["CUDAExecutionProvider"]
    kept, dropped = filter_available_providers(desired, available)
    assert kept == ["CUDAExecutionProvider", "CPUExecutionProvider"]
    assert dropped == []


def test_filter_available_providers_all_gpu_unavailable_degrades_to_cpu():
    desired = resolve_onnx_providers("rocm")
    available = ["CPUExecutionProvider"]
    kept, dropped = filter_available_providers(desired, available)
    assert kept == ["CPUExecutionProvider"]
    assert dropped == [
        "MIGraphXExecutionProvider",
        "ROCMExecutionProvider",
    ]


def test_available_onnx_providers_returns_cpu_on_import_error(monkeypatch):
    real_import = builtins.__import__

    def _raise_on_onnxruntime(name, *args, **kwargs):
        if name == "onnxruntime":
            raise ImportError("simulated broken onnxruntime")
        return real_import(name, *args, **kwargs)

    monkeypatch.setattr(builtins, "__import__", _raise_on_onnxruntime)
    assert available_onnx_providers() == ["CPUExecutionProvider"]


def test_get_dense_model_passes_cpu_only_when_gpu_unavailable(monkeypatch):
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

    monkeypatch.setattr(
        embedder_mod,
        "available_onnx_providers",
        lambda: ["CPUExecutionProvider"],
    )
    monkeypatch.setitem(
        sys.modules,
        "fastembed",
        SimpleNamespace(TextEmbedding=FakeTextEmbedding),
    )

    try:
        Embedder.release_models()
        _embedder(embed_device="rocm")._get_dense_model()
    finally:
        Embedder.release_models()

    assert calls == [{"providers": ["CPUExecutionProvider"]}]


def test_get_dense_model_logs_onnx_providers_filtered(caplog, monkeypatch):
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
            self.model = FakeInner(providers)

    monkeypatch.setattr(
        embedder_mod,
        "available_onnx_providers",
        lambda: ["CPUExecutionProvider"],
    )
    monkeypatch.setitem(
        sys.modules,
        "fastembed",
        SimpleNamespace(TextEmbedding=FakeTextEmbedding),
    )

    try:
        Embedder.release_models()
        with caplog.at_level(logging.WARNING, logger="codebase_indexer.indexer.embedder"):
            _embedder(embed_device="rocm")._get_dense_model()
    finally:
        Embedder.release_models()

    assert "onnx_providers_filtered" in caplog.text


def test_get_dense_model_falls_back_to_cpu_when_gpu_init_fails(caplog, monkeypatch):
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
            calls.append({"providers": list(providers)})
            if any(p != "CPUExecutionProvider" for p in providers):
                raise RuntimeError("simulated MIGraphX init failure")
            self.model = FakeInner(providers)

    monkeypatch.setattr(
        embedder_mod,
        "available_onnx_providers",
        lambda: list(_ALL_ONNX_PROVIDERS),
    )
    monkeypatch.setitem(
        sys.modules,
        "fastembed",
        SimpleNamespace(TextEmbedding=FakeTextEmbedding),
    )

    cache_key_providers = None
    try:
        Embedder.release_models()
        with caplog.at_level(logging.WARNING, logger="codebase_indexer.indexer.embedder"):
            e = _embedder(embed_device="rocm")
            model = e._get_dense_model()
            cache_key_providers = Embedder._shared_dense_cache_key[1]
            model_again = e._get_dense_model()
        assert model is model_again
    finally:
        Embedder.release_models()

    assert len(calls) == 2
    assert calls[0]["providers"] == [
        "MIGraphXExecutionProvider",
        "ROCMExecutionProvider",
        "CPUExecutionProvider",
    ]
    assert calls[1]["providers"] == ["CPUExecutionProvider"]
    assert "gpu_provider_init_failed_falling_back_to_cpu" in caplog.text
    assert cache_key_providers == ("CPUExecutionProvider",)
