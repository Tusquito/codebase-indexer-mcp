"""Unit tests for scripts/accelerator.py (ADR 0022 Phase 1)."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from scripts.accelerator import (  # noqa: E402
    GpuRequiredError,
    get_accelerator,
    require_gpu,
)


def test_get_accelerator_defaults_to_gpu():
    assert get_accelerator({}) == "gpu"


def test_get_accelerator_cpu_explicit():
    assert get_accelerator({"ACCELERATOR": "cpu"}) == "cpu"


def test_get_accelerator_invalid_raises():
    with pytest.raises(ValueError, match="Invalid ACCELERATOR"):
        get_accelerator({"ACCELERATOR": "auto"})


def test_require_gpu_noop_when_cpu():
    require_gpu(env={"ACCELERATOR": "cpu"}, check_nvidia=lambda: False)


def test_require_gpu_raises_when_nvidia_unavailable():
    with pytest.raises(GpuRequiredError, match="ACCELERATOR=gpu"):
        require_gpu(env={"ACCELERATOR": "gpu"}, check_nvidia=lambda: False)


def test_require_gpu_passes_when_nvidia_available():
    require_gpu(env={"ACCELERATOR": "gpu"}, check_nvidia=lambda: True)


def test_require_gpu_defaults_to_gpu_mode():
    with pytest.raises(GpuRequiredError):
        require_gpu(env={}, check_nvidia=lambda: False)
