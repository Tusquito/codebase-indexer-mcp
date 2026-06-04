"""Shared pytest fixtures."""

import pytest

# Values mirror .env.example (single place for deployment defaults).
_TEST_DENSE_EMBED_MODEL = "nomic-ai/nomic-embed-text-v1.5"
_TEST_SPARSE_EMBED_MODEL = "Qdrant/bm25"
_TEST_DENSE_EMBED_VECTOR_SIZE = "768"


@pytest.fixture(autouse=True)
def _required_embed_env(monkeypatch: pytest.MonkeyPatch) -> None:
    """Tests construct Settings() without repeating embed env vars."""
    monkeypatch.setenv("DENSE_EMBED_MODEL", _TEST_DENSE_EMBED_MODEL)
    monkeypatch.setenv("SPARSE_EMBED_MODEL", _TEST_SPARSE_EMBED_MODEL)
    monkeypatch.setenv("DENSE_EMBED_VECTOR_SIZE", _TEST_DENSE_EMBED_VECTOR_SIZE)
