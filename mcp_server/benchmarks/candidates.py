"""Loader + schema validation for the ADR 0026 candidate registry.

Reads ``benchmarks/fixtures/model_candidates.yaml`` (single source of truth for
the bake-off candidate pool) into typed :class:`Candidate` objects, validating
the schema and cross-checking native candidates against ``config.py`` so the
registry and the runtime embed registry cannot silently drift apart.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any

import yaml

from codebase_indexer.config import (
    KNOWN_EMBED_MODEL_DIMENSIONS,
    KNOWN_EMBED_MODEL_MAX_TOKENS,
    QWEN3_EMBED_SPECS,
)

REGISTRY_PATH = Path(__file__).resolve().parent / "fixtures" / "model_candidates.yaml"

VALID_STATUSES = frozenset(
    {"registered", "spike_passed", "dropped", "excluded"}
)
VALID_TEI_STATUS = frozenset({"native", "spike"})
# status values whose candidates are scored in the bake-off.
INCLUDED_STATUSES = frozenset({"registered", "spike_passed"})

_REQUIRED_FIELDS = (
    "model",
    "params",
    "native_dim",
    "vector_size",
    "context",
    "license",
    "tei_status",
    "status",
)


@dataclass(frozen=True)
class Candidate:
    """One dense-embedding candidate from the registry."""

    model: str
    params: str
    native_dim: int
    vector_size: int
    context: int
    license: str
    tei_status: str
    status: str
    role: str = "candidate"
    mrl_dims: tuple[int, ...] | None = None
    spike: str | None = None
    notes: str = ""
    rationale: str = ""

    @property
    def included(self) -> bool:
        """True when this candidate is scored in the bake-off."""
        return self.status in INCLUDED_STATUSES

    @property
    def is_mrl(self) -> bool:
        return bool(self.mrl_dims) and self.vector_size < self.native_dim


@dataclass(frozen=True)
class Registry:
    golden_set_version: str
    candidates: tuple[Candidate, ...]

    def __iter__(self):
        return iter(self.candidates)

    def __len__(self) -> int:
        return len(self.candidates)

    def by_model(self, model: str) -> Candidate:
        for cand in self.candidates:
            if cand.model == model:
                return cand
        raise KeyError(model)

    @property
    def included(self) -> tuple[Candidate, ...]:
        return tuple(c for c in self.candidates if c.included)

    @property
    def native(self) -> tuple[Candidate, ...]:
        return tuple(c for c in self.candidates if c.tei_status == "native")


class RegistryError(ValueError):
    """Raised when the candidate registry fails schema/consistency validation."""


def _parse_candidate(raw: dict[str, Any]) -> Candidate:
    missing = [f for f in _REQUIRED_FIELDS if f not in raw or raw[f] is None]
    if missing:
        model = raw.get("model", "<unknown>")
        raise RegistryError(f"candidate {model!r} missing fields: {missing}")

    status = str(raw["status"])
    if status not in VALID_STATUSES:
        raise RegistryError(
            f"candidate {raw['model']!r} has invalid status {status!r} "
            f"(allowed: {sorted(VALID_STATUSES)})"
        )
    tei_status = str(raw["tei_status"])
    if tei_status not in VALID_TEI_STATUS:
        raise RegistryError(
            f"candidate {raw['model']!r} has invalid tei_status {tei_status!r} "
            f"(allowed: {sorted(VALID_TEI_STATUS)})"
        )

    # A dropped/excluded candidate must document why.
    if status in {"dropped", "excluded"} and not str(raw.get("rationale", "")).strip():
        raise RegistryError(
            f"candidate {raw['model']!r} is {status} but has no rationale"
        )
    # A spike candidate must name the spike it went through.
    if tei_status == "spike" and not str(raw.get("spike", "")).strip():
        raise RegistryError(
            f"candidate {raw['model']!r} is a spike but does not name `spike`"
        )

    mrl = raw.get("mrl_dims")
    mrl_dims = tuple(int(d) for d in mrl) if mrl else None

    return Candidate(
        model=str(raw["model"]),
        params=str(raw["params"]),
        native_dim=int(raw["native_dim"]),
        vector_size=int(raw["vector_size"]),
        context=int(raw["context"]),
        license=str(raw["license"]),
        tei_status=tei_status,
        status=status,
        role=str(raw.get("role", "candidate")),
        mrl_dims=mrl_dims,
        spike=(str(raw["spike"]) if raw.get("spike") else None),
        notes=str(raw.get("notes", "") or "").strip(),
        rationale=str(raw.get("rationale", "") or "").strip(),
    )


def _check_config_consistency(candidate: Candidate) -> None:
    """Included native candidates must be wired into ``config.py``.

    Qwen3 models validate via MRL (native dim only in the registry), everything
    else must have a matching fixed-dimension + max-token registry entry.
    """
    if not candidate.included or candidate.tei_status != "native":
        return
    if candidate.model in QWEN3_EMBED_SPECS:
        native, _ = QWEN3_EMBED_SPECS[candidate.model]
        if native != candidate.native_dim:
            raise RegistryError(
                f"{candidate.model!r} native_dim {candidate.native_dim} != "
                f"QWEN3_EMBED_SPECS native {native}"
            )
        return
    dim = KNOWN_EMBED_MODEL_DIMENSIONS.get(candidate.model)
    if dim is None:
        raise RegistryError(
            f"{candidate.model!r} is an included native candidate but is missing "
            f"from KNOWN_EMBED_MODEL_DIMENSIONS (config.py)"
        )
    if dim != candidate.native_dim:
        raise RegistryError(
            f"{candidate.model!r} native_dim {candidate.native_dim} != "
            f"config dimension {dim}"
        )
    if candidate.model not in KNOWN_EMBED_MODEL_MAX_TOKENS:
        raise RegistryError(
            f"{candidate.model!r} missing from KNOWN_EMBED_MODEL_MAX_TOKENS (config.py)"
        )


def load_registry(path: Path | str | None = None) -> Registry:
    """Load and validate the candidate registry."""
    registry_path = Path(path) if path is not None else REGISTRY_PATH
    with registry_path.open("r", encoding="utf-8") as fh:
        raw = yaml.safe_load(fh)

    if not isinstance(raw, dict) or "candidates" not in raw:
        raise RegistryError("registry must be a mapping with a `candidates` list")

    candidates = tuple(_parse_candidate(c) for c in raw["candidates"])
    if not candidates:
        raise RegistryError("registry has no candidates")

    seen: set[str] = set()
    for cand in candidates:
        if cand.model in seen:
            raise RegistryError(f"duplicate candidate model: {cand.model!r}")
        seen.add(cand.model)
        _check_config_consistency(cand)

    return Registry(
        golden_set_version=str(raw.get("golden_set_version", "")),
        candidates=candidates,
    )


if __name__ == "__main__":  # pragma: no cover - manual sanity check
    reg = load_registry()
    print(f"golden_set_version={reg.golden_set_version} candidates={len(reg)}")
    for c in reg:
        flag = "scored" if c.included else c.status
        print(f"  [{flag:>12}] {c.model} dim={c.vector_size} ctx={c.context}")
