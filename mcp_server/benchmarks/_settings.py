"""Load Settings for benchmark harnesses with CI-safe embed defaults."""

from __future__ import annotations

from pathlib import Path

from codebase_indexer.config import Settings

# Match .env.example — required Settings fields have no Python defaults.
_BENCH_EMBED_DEFAULTS: dict[str, object] = {
    "dense_embed_model": "jinaai/jina-embeddings-v2-base-code",
    "tei_url": "http://127.0.0.1:8080",
    "sparse_embed_model": "Qdrant/bm25",
    "dense_embed_vector_size": 768,
    "sparse_threads": 2,
}


def load_settings(**overrides: object) -> Settings:
    """Build Settings, loading repo-root or mcp_server ``.env`` when present."""
    merged = {**_BENCH_EMBED_DEFAULTS, **overrides}
    env_candidates = (
        Path(__file__).resolve().parents[2] / ".env",
        Path(__file__).resolve().parents[1] / ".env",
    )
    env_file = next((p for p in env_candidates if p.is_file()), None)
    if env_file is not None:
        return Settings(_env_file=env_file, **merged)  # type: ignore[arg-type]
    return Settings(**merged)  # type: ignore[arg-type]
