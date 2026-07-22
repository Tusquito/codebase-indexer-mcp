#!/usr/bin/env python3
"""Canonical Aspire Docker Compose ``-f`` file list (ADR 0030 Phase 7).

Production default is Aspire/.NET only. Print for shell substitution::

    docker compose $(python scripts/aspire_compose.py) up -d --build
    docker compose $(python scripts/aspire_compose.py --neo4j) up -d --build
"""

from __future__ import annotations

import argparse
import os
import sys
from collections.abc import Mapping
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.accelerator import get_accelerator, require_gpu  # noqa: E402

ASPIRE_COMPOSE = "docker-compose.aspire.yml"
ASPIRE_NEO4J = "docker-compose.aspire.neo4j.yml"
ASPIRE_COLBERT_GPU = "docker-compose.aspire.colbert.gpu.yml"


def _truthy(value: str | None) -> bool:
    if value is None:
        return False
    return value.strip().lower() in ("1", "true", "yes", "on")


def aspire_compose_file_args(
    *,
    repo_root: Path | None = None,
    env: Mapping[str, str] | None = None,
    include_tei: bool = True,
    include_neo4j: bool | None = None,
    gpu_colbert: bool | None = None,
) -> list[str]:
    """Return ``-f`` arguments for the Aspire stack.

    When ``include_neo4j`` is None, Neo4j overlay is added if ``GRAPH_ENABLED``
    or ``Graph__Enabled`` is truthy. When ``gpu_colbert`` is None, the GPU
    ColBERT overlay is added when ``ACCELERATOR=gpu``.

    ``include_tei=False`` (external TEI / ADR 0029) still returns the base Aspire
    file; callers omit the ``tei`` service at ``up`` time and set
    ``Tei__Url=http://host.docker.internal:8080``.
    """
    del include_tei  # documented for harness parity; Aspire base always lists tei
    root = repo_root or REPO_ROOT
    source = env if env is not None else os.environ

    files: list[Path] = [root / ASPIRE_COMPOSE]

    if include_neo4j is None:
        include_neo4j = _truthy(source.get("GRAPH_ENABLED")) or _truthy(
            source.get("Graph__Enabled")
        )
    if include_neo4j:
        files.append(root / ASPIRE_NEO4J)

    if gpu_colbert is None:
        gpu_colbert = get_accelerator(source) == "gpu"
    if gpu_colbert:
        files.append(root / ASPIRE_COLBERT_GPU)

    args: list[str] = []
    for path in files:
        args.extend(["-f", str(path)])
    return args


def main() -> int:
    parser = argparse.ArgumentParser(description="Print Aspire compose -f args")
    parser.add_argument(
        "--neo4j",
        action="store_true",
        help="Force include docker-compose.aspire.neo4j.yml",
    )
    parser.add_argument(
        "--no-neo4j",
        action="store_true",
        help="Force omit Neo4j overlay",
    )
    parser.add_argument(
        "--gpu-colbert",
        action="store_true",
        help="Force include aspire ColBERT GPU overlay",
    )
    parser.add_argument(
        "--no-gpu-colbert",
        action="store_true",
        help="Force omit ColBERT GPU overlay",
    )
    args = parser.parse_args()

    require_gpu()
    neo4j: bool | None
    if args.neo4j:
        neo4j = True
    elif args.no_neo4j:
        neo4j = False
    else:
        neo4j = None

    gpu: bool | None
    if args.gpu_colbert:
        gpu = True
    elif args.no_gpu_colbert:
        gpu = False
    else:
        gpu = None

    print(" ".join(aspire_compose_file_args(include_neo4j=neo4j, gpu_colbert=gpu)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
