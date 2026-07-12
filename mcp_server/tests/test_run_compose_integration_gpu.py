"""Unit tests for GPU check in run_compose_integration (ADR 0025)."""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from scripts.compose_files import TEI_IMAGE_CPU_ARM64_DEFAULT, TEI_IMAGE_CPU_DEFAULT  # noqa: E402
from scripts.run_compose_integration import (  # noqa: E402
    check_tei_container_absent,
    check_tei_gpu_visible,
    preflight_host_tei,
    tei_embed_smoke,
    write_integration_env,
)


def _completed(returncode: int, stdout: str = "", stderr: str = "") -> subprocess.CompletedProcess:
    return subprocess.CompletedProcess(
        args=[],
        returncode=returncode,
        stdout=stdout,
        stderr=stderr,
    )


def test_tei_embed_smoke_passes_with_valid_response():
    def fake_embed(url: str, model: str, vector_size: int) -> tuple[bool, str]:
        assert url == "http://127.0.0.1:8080"
        assert model == "jinaai/jina-embeddings-v2-base-code"
        assert vector_size == 768
        return True, "ok"

    ok, detail = tei_embed_smoke(
        tei_url="http://127.0.0.1:8080",
        embed_fn=fake_embed,
    )
    assert ok is True
    assert detail == "ok"


def test_tei_embed_smoke_fails_on_embed_error():
    ok, detail = tei_embed_smoke(
        tei_url="http://127.0.0.1:8080",
        embed_fn=lambda _url, _model, _size: (False, "connection refused"),
    )
    assert ok is False
    assert "connection refused" in detail


def _is_nvidia_smi(cmd: list[str]) -> bool:
    return len(cmd) >= 2 and cmd[-1] == "nvidia-smi"


def test_check_tei_gpu_visible_passes_with_nvidia_smi():
    calls: list[list[str]] = []

    def fake_run(cmd: list[str], **_kwargs) -> subprocess.CompletedProcess:
        calls.append(cmd)
        if _is_nvidia_smi(cmd):
            return _completed(0, "NVIDIA-SMI 535.00\nGPU 0: RTX 4090")
        return _completed(1, "", "unexpected")

    ok, detail = check_tei_gpu_visible(
        run_cmd=fake_run,
        embed_fn=lambda _url, _model, _size: (True, "embedded"),
    )
    assert ok is True
    assert "NVIDIA" in detail
    assert any(_is_nvidia_smi(cmd) for cmd in calls)


def test_check_tei_gpu_visible_fails_when_nvidia_smi_missing():
    def fake_run(cmd: list[str], **_kwargs) -> subprocess.CompletedProcess:
        if _is_nvidia_smi(cmd):
            return _completed(127, "", "nvidia-smi not found")
        return _completed(1)

    ok, detail = check_tei_gpu_visible(
        run_cmd=fake_run,
        embed_fn=lambda _url, _model, _size: (True, "embedded"),
    )
    assert ok is False
    assert "nvidia-smi" in detail.lower()


def test_write_integration_env_sets_cpu_tei_image(monkeypatch, tmp_path):
    monkeypatch.setattr(
        "scripts.run_compose_integration.get_accelerator", lambda: "cpu"
    )
    monkeypatch.setattr(
        "scripts.run_compose_integration.tei_image_default",
        lambda _env: TEI_IMAGE_CPU_DEFAULT,
    )
    env_file = tmp_path / ".env.compose.integration"
    monkeypatch.setattr("scripts.run_compose_integration.ENV_FILE", env_file)

    write_integration_env(tmp_path / "workspace")

    content = env_file.read_text(encoding="utf-8")
    assert f"TEI_IMAGE={TEI_IMAGE_CPU_DEFAULT}" in content
    assert "ACCELERATOR=cpu" in content


def test_write_integration_env_sets_arm64_tei_image(monkeypatch, tmp_path):
    monkeypatch.setattr(
        "scripts.run_compose_integration.get_accelerator", lambda: "cpu"
    )
    monkeypatch.setattr(
        "scripts.run_compose_integration.tei_image_default",
        lambda _env: TEI_IMAGE_CPU_ARM64_DEFAULT,
    )
    env_file = tmp_path / ".env.compose.integration"
    monkeypatch.setattr("scripts.run_compose_integration.ENV_FILE", env_file)

    write_integration_env(tmp_path / "workspace")

    content = env_file.read_text(encoding="utf-8")
    assert f"TEI_IMAGE={TEI_IMAGE_CPU_ARM64_DEFAULT}" in content
    assert "TEI_MKL_INSTRUCTIONS=" not in content


def test_write_integration_env_sets_mkl_on_amd64_cpu(monkeypatch, tmp_path):
    monkeypatch.setattr(
        "scripts.run_compose_integration.get_accelerator", lambda: "cpu"
    )
    monkeypatch.setattr(
        "scripts.run_compose_integration.tei_image_default",
        lambda _env: TEI_IMAGE_CPU_DEFAULT,
    )
    monkeypatch.setattr(
        "scripts.run_compose_integration.container_arch", lambda: "amd64"
    )
    env_file = tmp_path / ".env.compose.integration"
    monkeypatch.setattr("scripts.run_compose_integration.ENV_FILE", env_file)

    write_integration_env(tmp_path / "workspace")

    content = env_file.read_text(encoding="utf-8")
    assert "TEI_MKL_INSTRUCTIONS=AVX2" in content


def test_write_integration_env_omits_tei_image_on_gpu(monkeypatch, tmp_path):
    monkeypatch.setattr(
        "scripts.run_compose_integration.get_accelerator", lambda: "gpu"
    )
    env_file = tmp_path / ".env.compose.integration"
    monkeypatch.setattr("scripts.run_compose_integration.ENV_FILE", env_file)

    write_integration_env(tmp_path / "workspace")

    content = env_file.read_text(encoding="utf-8")
    assert "TEI_IMAGE=" not in content
    assert "ACCELERATOR=gpu" in content


def test_check_tei_gpu_visible_fails_on_embed_error():
    def fake_run(cmd: list[str], **_kwargs) -> subprocess.CompletedProcess:
        if _is_nvidia_smi(cmd):
            return _completed(0, "NVIDIA-SMI")
        return _completed(1)

    ok, detail = check_tei_gpu_visible(
        run_cmd=fake_run,
        embed_fn=lambda _url, _model, _size: (False, "HTTP 503"),
    )
    assert ok is False
    assert "embed failed" in detail


def test_write_integration_env_external_tei_preset(monkeypatch, tmp_path):
    env_file = tmp_path / ".env.compose.integration"
    monkeypatch.setattr("scripts.run_compose_integration.ENV_FILE", env_file)

    write_integration_env(tmp_path / "workspace", external_tei=True)

    content = env_file.read_text(encoding="utf-8")
    assert "ACCELERATOR=cpu" in content
    assert "TEI_URL=http://host.docker.internal:8080" in content
    assert "MCP_MEM_LIMIT=12g" in content
    assert "QDRANT_MEM_LIMIT=8g" in content
    assert "COMPOSE_PROFILES=" not in content
    assert "TEI_IMAGE=" not in content
    assert "TEI_MEM_LIMIT=" not in content


def test_check_tei_container_absent_passes_when_no_container():
    def fake_run(cmd: list[str], **_kwargs) -> subprocess.CompletedProcess:
        assert cmd[:3] == ["docker", "ps", "--filter"]
        return _completed(0, "")

    ok, detail = check_tei_container_absent(run_cmd=fake_run)
    assert ok is True
    assert "no bundled tei container" in detail


def test_check_tei_container_absent_fails_when_container_running():
    def fake_run(cmd: list[str], **_kwargs) -> subprocess.CompletedProcess:
        return _completed(0, "codeindexer_tei\n")

    ok, detail = check_tei_container_absent(run_cmd=fake_run)
    assert ok is False
    assert "codeindexer_tei" in detail


def test_preflight_host_tei_fails_when_unhealthy(monkeypatch):
    monkeypatch.setattr(
        "scripts.run_compose_integration.http_ok",
        lambda _url, timeout=3.0: (False, "connection refused"),
    )

    ok, detail = preflight_host_tei()
    assert ok is False
    assert "host TEI not healthy" in detail
    assert "127.0.0.1:8080" in detail


def test_preflight_host_tei_passes_when_healthy(monkeypatch):
    monkeypatch.setattr(
        "scripts.run_compose_integration.http_ok",
        lambda _url, timeout=3.0: (True, "ok"),
    )

    ok, detail = preflight_host_tei()
    assert ok is True
    assert detail == "ok"
