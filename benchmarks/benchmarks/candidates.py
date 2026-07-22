"""ADR 0026 candidate registry — deferred MCP-HTTP / offline port.

The Python ``codebase_indexer.config`` embed registry was removed with the
MCP runtime (ADR 0030 Phase 7). Bake-off tooling that needs native dimension
cross-checks should be re-ported against ``KnownEmbedModels`` appsettings.
"""

from __future__ import annotations

import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import yaml

REGISTRY_PATH = Path(__file__).resolve().parent / "fixtures" / "model_candidates.yaml"
VALID_STATUSES = frozenset({"registered", "spike_passed", "dropped", "excluded"})
VALID_TEI_STATUS = frozenset({"native", "spike"})
INCLUDED_STATUSES = frozenset({"registered", "spike_passed"})


@dataclass(frozen=True)
class Candidate:
    id: str
    model: str
    params: str
    native_dim: int
    vector_size: int
    context: int
    license: str
    tei_status: str
    status: str
    notes: str = ""


def load_candidates(path: Path | None = None) -> list[Candidate]:
    """Load candidates from YAML without Python runtime registry cross-check."""
    registry = path or REGISTRY_PATH
    raw = yaml.safe_load(registry.read_text(encoding="utf-8")) or {}
    items = raw.get("candidates") or []
    out: list[Candidate] = []
    for item in items:
        if not isinstance(item, dict) or "id" not in item:
            continue
        out.append(
            Candidate(
                id=str(item["id"]),
                model=str(item["model"]),
                params=str(item.get("params", "")),
                native_dim=int(item["native_dim"]),
                vector_size=int(item["vector_size"]),
                context=int(item["context"]),
                license=str(item.get("license", "")),
                tei_status=str(item.get("tei_status", "native")),
                status=str(item.get("status", "registered")),
                notes=str(item.get("notes", "")),
            )
        )
    return out


def included_candidates(path: Path | None = None) -> list[Candidate]:
    return [c for c in load_candidates(path) if c.status in INCLUDED_STATUSES]


if __name__ == "__main__":
    for c in load_candidates():
        print(f"{c.id}\t{c.model}\t{c.vector_size}\t{c.status}")
    sys.exit(0)
