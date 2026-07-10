"""Config-registry tests for ADR 0026 Phase 3 native candidate specs."""

import pytest
from pydantic import ValidationError

from codebase_indexer.config import (
    GRANITE_EMBED_SPECS,
    GTE_MODERNBERT_SPECS,
    INF_RETRIEVER_SPECS,
    KNOWN_EMBED_MODEL_DIMENSIONS,
    KNOWN_EMBED_MODEL_MAX_TOKENS,
    Settings,
)


def _settings(model: str, dim: int) -> Settings:
    return Settings(
        dense_embed_model=model,
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=dim,
        sparse_threads=2,
    )


def test_gte_modernbert_specs():
    assert GTE_MODERNBERT_SPECS["Alibaba-NLP/gte-modernbert-base"] == (768, 8192)
    for model, (dim, max_tokens) in GTE_MODERNBERT_SPECS.items():
        assert KNOWN_EMBED_MODEL_DIMENSIONS[model] == dim
        assert KNOWN_EMBED_MODEL_MAX_TOKENS[model] == max_tokens


def test_granite_specs():
    assert GRANITE_EMBED_SPECS[
        "ibm-granite/granite-embedding-311m-multilingual-r2"
    ] == (768, 32768)
    assert GRANITE_EMBED_SPECS[
        "ibm-granite/granite-embedding-97m-multilingual-r2"
    ] == (384, 32768)
    for model, (dim, max_tokens) in GRANITE_EMBED_SPECS.items():
        assert KNOWN_EMBED_MODEL_DIMENSIONS[model] == dim
        assert KNOWN_EMBED_MODEL_MAX_TOKENS[model] == max_tokens


def test_inf_retriever_specs():
    assert INF_RETRIEVER_SPECS["infly/inf-retriever-v1-1.5b"] == (1536, 32768)
    for model, (dim, max_tokens) in INF_RETRIEVER_SPECS.items():
        assert KNOWN_EMBED_MODEL_DIMENSIONS[model] == dim
        assert KNOWN_EMBED_MODEL_MAX_TOKENS[model] == max_tokens


def test_gte_modernbert_vector_size_valid():
    s = _settings("Alibaba-NLP/gte-modernbert-base", 768)
    assert s.dense_embed_vector_size == 768


def test_granite_311m_vector_size_valid():
    s = _settings("ibm-granite/granite-embedding-311m-multilingual-r2", 768)
    assert s.dense_embed_vector_size == 768


def test_granite_97m_vector_size_valid():
    s = _settings("ibm-granite/granite-embedding-97m-multilingual-r2", 384)
    assert s.dense_embed_vector_size == 384


def test_inf_retriever_vector_size_valid():
    s = _settings("infly/inf-retriever-v1-1.5b", 1536)
    assert s.dense_embed_vector_size == 1536


def test_granite_97m_wrong_vector_size_rejected():
    with pytest.raises((ValueError, ValidationError), match="does not match"):
        _settings("ibm-granite/granite-embedding-97m-multilingual-r2", 768)


def test_gte_modernbert_wrong_vector_size_rejected():
    with pytest.raises((ValueError, ValidationError), match="does not match"):
        _settings("Alibaba-NLP/gte-modernbert-base", 384)
