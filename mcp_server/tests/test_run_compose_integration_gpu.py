"""Unit tests for GPU processor check in run_compose_integration (ADR 0022 Phase 3)."""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from scripts.run_compose_integration import (  # noqa: E402
    check_ollama_gpu_processor,
    ollama_ps_shows_gpu,
)


def _completed(returncode: int, stdout: str = "", stderr: str = "") -> subprocess.CompletedProcess:
    return subprocess.CompletedProcess(
        args=[],
        returncode=returncode,
        stdout=stdout,
        stderr=stderr,
    )


def test_ollama_ps_shows_gpu_when_processor_is_gpu():
    ps_out = "NAME    ID    SIZE    PROCESSOR    UNTIL\njina    abc   1 GB    100% GPU     4m"
    assert ollama_ps_shows_gpu(ps_out) is True


def test_ollama_ps_shows_gpu_false_for_cpu_only():
    ps_out = "NAME    ID    SIZE    PROCESSOR    UNTIL\njina    abc   1 GB    100% CPU     4m"
    assert ollama_ps_shows_gpu(ps_out) is False


def test_ollama_ps_shows_gpu_false_when_no_models_loaded():
    assert ollama_ps_shows_gpu("NAME    ID    SIZE    PROCESSOR    UNTIL") is False


def _is_ollama_pull(cmd: list[str]) -> bool:
    return len(cmd) >= 3 and cmd[-2] == "pull" and cmd[-3] == "ollama"


def _is_ollama_ps(cmd: list[str]) -> bool:
    return len(cmd) >= 1 and cmd[-1] == "ps" and cmd[-2] == "ollama"


def test_check_ollama_gpu_processor_passes_with_gpu_ps():
    calls: list[list[str]] = []

    def fake_run(cmd: list[str], **_kwargs) -> subprocess.CompletedProcess:
        calls.append(cmd)
        if _is_ollama_pull(cmd):
            return _completed(0, "pull ok")
        if _is_ollama_ps(cmd):
            return _completed(
                0,
                "NAME    ID    SIZE    PROCESSOR    UNTIL\n"
                "jina    abc   1 GB    100% GPU     4m",
            )
        return _completed(1, "", "unexpected")

    ok, detail = check_ollama_gpu_processor(
        run_cmd=fake_run,
        embed_fn=lambda _url, _model: (True, "embedded"),
    )
    assert ok is True
    assert "GPU" in detail
    assert any(_is_ollama_pull(cmd) for cmd in calls)
    assert any(_is_ollama_ps(cmd) for cmd in calls)


def test_check_ollama_gpu_processor_fails_when_ps_is_cpu():
    def fake_run(cmd: list[str], **_kwargs) -> subprocess.CompletedProcess:
        if _is_ollama_pull(cmd):
            return _completed(0)
        if _is_ollama_ps(cmd):
            return _completed(
                0,
                "NAME    ID    SIZE    PROCESSOR    UNTIL\n"
                "jina    abc   1 GB    100% CPU     4m",
            )
        return _completed(1)

    ok, detail = check_ollama_gpu_processor(
        run_cmd=fake_run,
        embed_fn=lambda _url, _model: (True, "embedded"),
    )
    assert ok is False
    assert "PROCESSOR not GPU" in detail


def test_check_ollama_gpu_processor_fails_on_pull_error():
    def fake_run(_cmd: list[str], **_kwargs) -> subprocess.CompletedProcess:
        return _completed(1, "", "pull failed")

    ok, detail = check_ollama_gpu_processor(
        run_cmd=fake_run,
        embed_fn=lambda _url, _model: (True, "embedded"),
    )
    assert ok is False
    assert "pull failed" in detail


def test_check_ollama_gpu_processor_fails_on_embed_error():
    def fake_run(cmd: list[str], **_kwargs) -> subprocess.CompletedProcess:
        if _is_ollama_pull(cmd):
            return _completed(0)
        return _completed(1)

    ok, detail = check_ollama_gpu_processor(
        run_cmd=fake_run,
        embed_fn=lambda _url, _model: (False, "connection refused"),
    )
    assert ok is False
    assert "embed failed" in detail
