"""Unit tests for scripts/aspire_compose.py and accelerator TEI defaults (ADR 0030 Phase 7)."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import patch

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from scripts.accelerator import (  # noqa: E402
    TEI_IMAGE_CPU_ARM64_DEFAULT,
    container_arch,
    tei_image_default,
)
from scripts.aspire_compose import aspire_compose_file_args  # noqa: E402


def _paths(args: list[str]) -> list[Path]:
    return [Path(args[i + 1]) for i in range(0, len(args), 2) if args[i] == "-f"]


def test_default_aspire_base_only_on_cpu():
    with patch("scripts.aspire_compose.get_accelerator", return_value="cpu"):
        args = aspire_compose_file_args(
            repo_root=REPO_ROOT, env={"ACCELERATOR": "cpu"}, gpu_colbert=False
        )
    paths = _paths(args)
    assert paths == [REPO_ROOT / "docker-compose.aspire.yml"]


def test_gpu_adds_colbert_overlay():
    args = aspire_compose_file_args(
        repo_root=REPO_ROOT, env={"ACCELERATOR": "gpu"}, gpu_colbert=True
    )
    paths = _paths(args)
    assert REPO_ROOT / "docker-compose.aspire.yml" in paths
    assert REPO_ROOT / "docker-compose.aspire.colbert.gpu.yml" in paths


def test_neo4j_overlay():
    args = aspire_compose_file_args(
        repo_root=REPO_ROOT,
        env={},
        include_neo4j=True,
        gpu_colbert=False,
    )
    paths = _paths(args)
    assert REPO_ROOT / "docker-compose.aspire.neo4j.yml" in paths


def test_tei_image_default_gpu():
    assert "89-1.9" in tei_image_default({})


def test_tei_image_default_cpu():
    with patch("scripts.accelerator.container_arch", return_value="amd64"):
        assert tei_image_default({"ACCELERATOR": "cpu"}).endswith("cpu-1.9")


def test_tei_image_default_arm64():
    with patch("scripts.accelerator.container_arch", return_value="arm64"):
        assert tei_image_default({"ACCELERATOR": "cpu"}) == TEI_IMAGE_CPU_ARM64_DEFAULT


def test_tei_image_default_respects_explicit_override():
    custom = "ghcr.io/example/custom:tag"
    with patch("scripts.accelerator.container_arch", return_value="arm64"):
        assert tei_image_default({"ACCELERATOR": "cpu", "TEI_IMAGE": custom}) == custom


def test_container_arch_prefers_docker_server_arch():
    with patch(
        "scripts.accelerator.subprocess.run",
        return_value=type("P", (), {"returncode": 0, "stdout": "arm64\n"})(),
    ):
        with patch("scripts.accelerator.platform.machine", return_value="x86_64"):
            assert container_arch() == "arm64"


def test_container_arch_falls_back_to_platform_machine():
    with patch(
        "scripts.accelerator.subprocess.run",
        return_value=type("P", (), {"returncode": 1, "stdout": ""})(),
    ):
        with patch("scripts.accelerator.platform.machine", return_value="aarch64"):
            assert container_arch() == "arm64"
