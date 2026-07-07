"""Unit tests for scripts/compose_files.py (ADR 0022 Phase 1)."""

from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from scripts.compose_files import compose_file_args, tei_image_default  # noqa: E402


def _paths(args: list[str]) -> list[Path]:
    return [Path(args[i + 1]) for i in range(0, len(args), 2) if args[i] == "-f"]


def test_default_includes_gpu_tei_override():
    args = compose_file_args(repo_root=REPO_ROOT, env={})
    paths = _paths(args)
    assert paths == [
        REPO_ROOT / "docker-compose.yml",
        REPO_ROOT / "docker-compose.tei.yml",
        REPO_ROOT / "docker-compose.tei.gpu.yml",
    ]


def test_accelerator_cpu_omits_gpu_files():
    args = compose_file_args(repo_root=REPO_ROOT, env={"ACCELERATOR": "cpu"})
    paths = _paths(args)
    assert paths == [
        REPO_ROOT / "docker-compose.yml",
        REPO_ROOT / "docker-compose.tei.yml",
    ]
    assert not any(".gpu.yml" in str(p) for p in paths)


def test_tei_image_default_gpu():
    assert "89-1.9" in tei_image_default({})


def test_tei_image_default_cpu():
    assert tei_image_default({"ACCELERATOR": "cpu"}).endswith("cpu-1.9")


def test_remote_colbert_sidecar_includes_worker_files():
    env = {
        "ACCELERATOR": "gpu",
        "RERANK_ENABLED": "true",
        "COLBERT_EMBED_BACKEND": "remote",
    }
    args = compose_file_args(repo_root=REPO_ROOT, env=env)
    paths = _paths(args)
    assert REPO_ROOT / "docker-compose.colbert-worker.yml" in paths
    assert REPO_ROOT / "docker-compose.colbert-worker.gpu.yml" in paths


def test_remote_colbert_cpu_omits_gpu_worker_override():
    env = {
        "ACCELERATOR": "cpu",
        "RERANK_ENABLED": "true",
        "COLBERT_EMBED_BACKEND": "remote",
    }
    args = compose_file_args(repo_root=REPO_ROOT, env=env)
    paths = _paths(args)
    assert REPO_ROOT / "docker-compose.colbert-worker.yml" in paths
    assert REPO_ROOT / "docker-compose.colbert-worker.gpu.yml" not in paths


def test_rerank_on_defaults_remote_sidecar_compose():
    env = {
        "ACCELERATOR": "gpu",
        "RERANK_ENABLED": "true",
    }
    args = compose_file_args(repo_root=REPO_ROOT, env=env)
    paths = _paths(args)
    assert REPO_ROOT / "docker-compose.colbert-worker.yml" in paths
    assert REPO_ROOT / "docker-compose.colbert-worker.gpu.yml" in paths


def test_rerank_onnx_does_not_add_sidecar_compose():
    env = {
        "ACCELERATOR": "gpu",
        "RERANK_ENABLED": "true",
        "COLBERT_EMBED_BACKEND": "onnx",
    }
    args = compose_file_args(repo_root=REPO_ROOT, env=env)
    paths = _paths(args)
    assert not any("colbert-worker" in str(p) for p in paths)
