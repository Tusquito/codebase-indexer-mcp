#!/usr/bin/env python3
"""Canonical Docker Compose ``-f`` file list (ADR 0022).

Print for shell substitution::

    docker compose $(python scripts/compose_files.py) --profile bundled-tei up -d --build
"""

from __future__ import annotations

import os
import platform
import subprocess
import sys
from collections.abc import Mapping
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.accelerator import get_accelerator

REPO_ROOT = Path(__file__).resolve().parents[1]

TEI_IMAGE_GPU_DEFAULT = "ghcr.io/huggingface/text-embeddings-inference:89-1.9"
TEI_IMAGE_CPU_DEFAULT = "ghcr.io/huggingface/text-embeddings-inference:cpu-1.9"
TEI_IMAGE_CPU_ARM64_DEFAULT = (
    "ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-latest"
)

_ARM64_ALIASES = frozenset({"arm64", "aarch64"})
_AMD64_ALIASES = frozenset({"amd64", "x86_64", "x86"})


def _truthy(value: str | None) -> bool:
    if value is None:
        return False
    return value.strip().lower() in ("1", "true", "yes", "on")


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


def _resolve_colbert_backend(env: Mapping[str, str]) -> str:
    explicit = env.get("COLBERT_EMBED_BACKEND")
    if explicit is not None and explicit.strip():
        return explicit.strip().lower()
    if _truthy(env.get("RERANK_ENABLED")):
        return "remote"
    return "onnx"


def _colbert_sidecar_enabled(env: Mapping[str, str]) -> bool:
    if not _truthy(env.get("RERANK_ENABLED")):
        return False
    return _resolve_colbert_backend(env) == "remote"


def tei_image_default(env: Mapping[str, str]) -> str:
    """Return default TEI image tag for the active accelerator and container arch."""
    explicit = env.get("TEI_IMAGE")
    if explicit is not None and explicit.strip():
        return explicit.strip()
    if get_accelerator(env) == "gpu":
        return TEI_IMAGE_GPU_DEFAULT
    if container_arch() == "arm64":
        return TEI_IMAGE_CPU_ARM64_DEFAULT
    return TEI_IMAGE_CPU_DEFAULT


def compose_file_args(
    *,
    repo_root: Path | None = None,
    env: Mapping[str, str] | None = None,
    include_tei: bool = True,
    include_neo4j: bool | None = None,
) -> list[str]:
    """Return ``-f`` arguments for the active accelerator and ColBERT sidecar mode.

    When ``include_neo4j`` is None, the Neo4j override is added if ``GRAPH_ENABLED``
    is truthy in the environment; pass True/False to force it on/off.
    """
    root = repo_root or REPO_ROOT
    source = env if env is not None else os.environ
    use_gpu = get_accelerator(source) == "gpu"

    files: list[Path] = [root / "docker-compose.yml"]
    if include_tei:
        files.append(root / "docker-compose.tei.yml")
        if use_gpu:
            files.append(root / "docker-compose.tei.gpu.yml")
        elif container_arch() == "amd64":
            files.append(root / "docker-compose.tei.amd64-mkl.yml")

    if _colbert_sidecar_enabled(source):
        files.append(root / "docker-compose.colbert-worker.yml")
        if use_gpu:
            files.append(root / "docker-compose.colbert-worker.gpu.yml")

    if include_neo4j is None:
        include_neo4j = _truthy(source.get("GRAPH_ENABLED"))
    if include_neo4j:
        files.append(root / "docker-compose.neo4j.yml")

    args: list[str] = []
    for path in files:
        args.extend(["-f", str(path)])
    return args


def main() -> int:
    print(" ".join(compose_file_args()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
