#!/usr/bin/env python3
"""Canonical Docker Compose ``-f`` file list (ADR 0022).

Print for shell substitution::

    docker compose $(python scripts/compose_files.py) --profile bundled-ollama up -d --build
"""

from __future__ import annotations

import os
import sys
from collections.abc import Mapping
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.accelerator import get_accelerator

REPO_ROOT = Path(__file__).resolve().parents[1]


def _truthy(value: str | None) -> bool:
    if value is None:
        return False
    return value.strip().lower() in ("1", "true", "yes", "on")


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


def compose_file_args(
    *,
    repo_root: Path | None = None,
    env: Mapping[str, str] | None = None,
    include_ollama: bool = True,
) -> list[str]:
    """Return ``-f`` arguments for the active accelerator and ColBERT sidecar mode."""
    root = repo_root or REPO_ROOT
    source = env if env is not None else os.environ
    use_gpu = get_accelerator(source) == "gpu"

    files: list[Path] = [root / "docker-compose.yml"]
    if include_ollama:
        files.append(root / "docker-compose.ollama.yml")
        if use_gpu:
            files.append(root / "docker-compose.ollama.gpu.yml")

    if _colbert_sidecar_enabled(source):
        files.append(root / "docker-compose.colbert-worker.yml")
        if use_gpu:
            files.append(root / "docker-compose.colbert-worker.gpu.yml")

    args: list[str] = []
    for path in files:
        args.extend(["-f", str(path)])
    return args


def main() -> int:
    print(" ".join(compose_file_args()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
