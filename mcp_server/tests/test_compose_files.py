"""Unit tests for scripts/compose_files.py (ADR 0022 Phase 1, ADR 0028 Phase 2)."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import patch

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from scripts.compose_files import (  # noqa: E402
    TEI_IMAGE_CPU_ARM64_DEFAULT,
    TEI_IMAGE_CPU_DEFAULT,
    compose_file_args,
    container_arch,
    tei_image_default,
)


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
    with patch("scripts.compose_files.container_arch", return_value="amd64"):
        args = compose_file_args(repo_root=REPO_ROOT, env={"ACCELERATOR": "cpu"})
    paths = _paths(args)
    assert paths == [
        REPO_ROOT / "docker-compose.yml",
        REPO_ROOT / "docker-compose.tei.yml",
        REPO_ROOT / "docker-compose.tei.amd64-mkl.yml",
    ]
    assert not any(".gpu.yml" in str(p) for p in paths)


def test_tei_image_default_gpu():
    assert "89-1.9" in tei_image_default({})


def test_tei_image_default_cpu():
    with patch("scripts.compose_files.container_arch", return_value="amd64"):
        assert tei_image_default({"ACCELERATOR": "cpu"}).endswith("cpu-1.9")


def test_tei_image_default_arm64():
    with patch("scripts.compose_files.container_arch", return_value="arm64"):
        assert tei_image_default({"ACCELERATOR": "cpu"}) == TEI_IMAGE_CPU_ARM64_DEFAULT


def test_tei_image_default_respects_explicit_override():
    custom = "ghcr.io/example/custom:tag"
    with patch("scripts.compose_files.container_arch", return_value="arm64"):
        assert tei_image_default({"ACCELERATOR": "cpu", "TEI_IMAGE": custom}) == custom


def test_container_arch_prefers_docker_server_arch():
    with patch(
        "scripts.compose_files.subprocess.run",
        return_value=type("P", (), {"returncode": 0, "stdout": "arm64\n"})(),
    ):
        with patch("scripts.compose_files.platform.machine", return_value="x86_64"):
            assert container_arch() == "arm64"


def test_container_arch_falls_back_to_platform_machine():
    with patch(
        "scripts.compose_files.subprocess.run",
        return_value=type("P", (), {"returncode": 1, "stdout": ""})(),
    ):
        with patch("scripts.compose_files.platform.machine", return_value="aarch64"):
            assert container_arch() == "arm64"


def test_cpu_arm64_omits_mkl_overlay():
    with patch("scripts.compose_files.container_arch", return_value="arm64"):
        args = compose_file_args(repo_root=REPO_ROOT, env={"ACCELERATOR": "cpu"})
    paths = _paths(args)
    assert REPO_ROOT / "docker-compose.tei.amd64-mkl.yml" not in paths


def test_cpu_amd64_includes_mkl_overlay():
    with patch("scripts.compose_files.container_arch", return_value="amd64"):
        args = compose_file_args(repo_root=REPO_ROOT, env={"ACCELERATOR": "cpu"})
    paths = _paths(args)
    assert REPO_ROOT / "docker-compose.tei.amd64-mkl.yml" in paths


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
