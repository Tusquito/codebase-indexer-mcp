#!/usr/bin/env python3
"""Compose-only accelerator mode (ACCELERATOR env var).

Default is GPU; CPU is explicit via ACCELERATOR=cpu only (ADR 0022).

Also hosts TEI image / container-arch defaults previously in
``scripts/compose_files.py`` (deleted at ADR 0030 Phase 7).
"""

from __future__ import annotations

import os
import platform
import subprocess
from collections.abc import Callable, Mapping
from typing import Final

VALID_ACCELERATORS: Final[frozenset[str]] = frozenset({"gpu", "cpu"})

TEI_IMAGE_GPU_DEFAULT = "ghcr.io/huggingface/text-embeddings-inference:89-1.9"
TEI_IMAGE_CPU_DEFAULT = "ghcr.io/huggingface/text-embeddings-inference:cpu-1.9"
TEI_IMAGE_CPU_ARM64_DEFAULT = (
    "ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-latest"
)

_ARM64_ALIASES = frozenset({"arm64", "aarch64"})
_AMD64_ALIASES = frozenset({"amd64", "x86_64", "x86"})


def _normalize_arch(raw: str) -> str:
    value = raw.strip().lower()
    if value in _ARM64_ALIASES:
        return "arm64"
    if value in _AMD64_ALIASES:
        return "amd64"
    return value


def container_arch() -> str:
    """Return container runtime arch: Docker Server.Arch, else ``platform.machine()``."""
    try:
        proc = subprocess.run(
            ["docker", "version", "--format", "{{.Server.Arch}}"],
            capture_output=True,
            text=True,
            timeout=5,
            check=False,
        )
        if proc.returncode == 0:
            arch = proc.stdout.strip()
            if arch:
                return _normalize_arch(arch)
    except (OSError, subprocess.TimeoutExpired):
        pass
    return _normalize_arch(platform.machine())


def tei_image_default(env: Mapping[str, str] | None = None) -> str:
    """Return default TEI image tag for the active accelerator and container arch."""
    source = env if env is not None else os.environ
    explicit = source.get("TEI_IMAGE")
    if explicit is not None and explicit.strip():
        return explicit.strip()
    if get_accelerator(source) == "gpu":
        return TEI_IMAGE_GPU_DEFAULT
    if container_arch() == "arm64":
        return TEI_IMAGE_CPU_ARM64_DEFAULT
    return TEI_IMAGE_CPU_DEFAULT


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
