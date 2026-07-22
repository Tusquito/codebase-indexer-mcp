"""Minimal settings for benchmark harnesses (no MCP runtime)."""

from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class BenchSettings:
    """Subset of runtime config needed for Qdrant label checks / MCP eval."""

    qdrant_url: str = "http://127.0.0.1:6333"
    tei_url: str = "http://127.0.0.1:8080"
    dense_embed_model: str = "jinaai/jina-embeddings-v2-base-code"
    sparse_embed_model: str = "Qdrant/bm25"
    dense_embed_vector_size: int = 768
    sparse_threads: int = 2
    hybrid_search: bool = True
    rerank_enabled: bool = False


def _load_dotenv(path: Path) -> dict[str, str]:
    if not path.is_file():
        return {}
    out: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        out[key.strip()] = value.strip().strip('"').strip("'")
    return out


def load_settings(**overrides: object) -> BenchSettings:
    """Build settings from env / optional ``.env``, with bench defaults."""
    env_candidates = (
        Path(__file__).resolve().parents[2] / ".env",
        Path(__file__).resolve().parents[1] / ".env",
    )
    file_env: dict[str, str] = {}
    for candidate in env_candidates:
        if candidate.is_file():
            file_env = _load_dotenv(candidate)
            break

    def _get(key: str, default: str) -> str:
        if key in os.environ and os.environ[key].strip():
            return os.environ[key].strip()
        if key in file_env and file_env[key].strip():
            return file_env[key].strip()
        return default

    base = BenchSettings(
        qdrant_url=_get("QDRANT_URL", BenchSettings.qdrant_url),
        tei_url=_get("TEI_URL", BenchSettings.tei_url),
        dense_embed_model=_get("DENSE_EMBED_MODEL", BenchSettings.dense_embed_model),
        sparse_embed_model=_get("SPARSE_EMBED_MODEL", BenchSettings.sparse_embed_model),
        dense_embed_vector_size=int(
            _get("DENSE_EMBED_VECTOR_SIZE", str(BenchSettings.dense_embed_vector_size))
        ),
        sparse_threads=int(_get("SPARSE_THREADS", str(BenchSettings.sparse_threads))),
        hybrid_search=_get("HYBRID_SEARCH", "true").lower()
        in ("1", "true", "yes", "on"),
        rerank_enabled=_get("RERANK_ENABLED", "false").lower()
        in ("1", "true", "yes", "on"),
    )
    # Apply overrides (snake_case field names).
    data = {**base.__dict__, **{k: v for k, v in overrides.items() if hasattr(base, k)}}
    return BenchSettings(**data)  # type: ignore[arg-type]


def load_settings_for_candidate(candidate: object, **overrides: object) -> BenchSettings:
    """Swap dense model/size for an ADR 0026 bake-off candidate."""
    model = getattr(candidate, "model")
    vector_size = getattr(candidate, "vector_size")
    return load_settings(
        dense_embed_model=model,
        dense_embed_vector_size=vector_size,
        **overrides,
    )
