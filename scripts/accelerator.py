#!/usr/bin/env python3
"""Compose-only accelerator mode (ACCELERATOR env var).

Default is GPU; CPU is explicit via ACCELERATOR=cpu only (ADR 0022).
"""

from __future__ import annotations

import os
import subprocess
from collections.abc import Callable, Mapping
from typing import Final

VALID_ACCELERATORS: Final[frozenset[str]] = frozenset({"gpu", "cpu"})


class GpuRequiredError(RuntimeError):
    """Raised when ACCELERATOR=gpu but NVIDIA Container Toolkit is unavailable."""


def get_accelerator(env: Mapping[str, str] | None = None) -> str:
    """Return ``gpu`` (default) or ``cpu`` from ACCELERATOR."""
    source = env if env is not None else os.environ
    raw = (source.get("ACCELERATOR") or "gpu").strip().lower()
    if raw not in VALID_ACCELERATORS:
        raise ValueError(
            f"Invalid ACCELERATOR={raw!r}; expected one of {sorted(VALID_ACCELERATORS)}"
        )
    return raw


def nvidia_docker_available() -> bool:
    """True when ``docker info`` reports the NVIDIA runtime."""
    try:
        proc = subprocess.run(
            ["docker", "info"],
            capture_output=True,
            text=True,
            timeout=15,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return False
    if proc.returncode != 0:
        return False
    combined = f"{proc.stdout}\n{proc.stderr}".lower()
    return "nvidia" in combined


def require_gpu(
    *,
    env: Mapping[str, str] | None = None,
    check_nvidia: Callable[[], bool] | None = None,
) -> None:
    """Fail fast when default GPU mode is requested but NVIDIA is unavailable."""
    if get_accelerator(env) != "gpu":
        return
    checker = check_nvidia or nvidia_docker_available
    if checker():
        return
    raise GpuRequiredError(
        "ACCELERATOR=gpu (default) requires NVIDIA Container Toolkit and a working "
        "NVIDIA Docker runtime. Install "
        "https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html "
        "or set ACCELERATOR=cpu for CPU-only hosts (CI, air-gapped servers)."
    )
