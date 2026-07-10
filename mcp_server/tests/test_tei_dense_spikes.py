"""ADR 0026 Phase 3 spike-hook tests for TeiDenseBackend.

Covers the two feature-flagged hooks landed for the integration spikes:
  * instruction_prefix (inf-retriever)  — query-side task prefix
  * normalize_output   (pplx infra)     — L2 normalization of returned vectors

Both default OFF: the Jina/default path is asserted unchanged.
"""

from unittest.mock import AsyncMock, MagicMock

import pytest

from codebase_indexer.indexer.backends.tei_dense import TeiDenseBackend


def _openai_response(embeddings):
    return {
        "data": [
            {"embedding": vec, "index": idx} for idx, vec in enumerate(embeddings)
        ],
        "model": "spike",
    }


def _client_capturing(inputs, embeddings):
    async def fake_post(url, json=None):
        inputs.append(json["input"])
        resp = MagicMock()
        resp.status_code = 200
        resp.raise_for_status = MagicMock()
        resp.json = MagicMock(return_value=_openai_response(embeddings))
        return resp

    client = MagicMock()
    client.post = AsyncMock(side_effect=fake_post)
    return client


def _backend(**kwargs) -> TeiDenseBackend:
    defaults = dict(model_name="spike/model", vector_size=2, batch_size=10)
    defaults.update(kwargs)
    b = TeiDenseBackend(**defaults)
    b._ready = True
    return b


def test_defaults_leave_hooks_off():
    b = _backend()
    assert b._query_instruction == ""
    assert b._normalize_output is False


@pytest.mark.asyncio
async def test_embed_query_applies_instruction_prefix():
    inputs: list = []
    b = _backend(query_instruction="Represent this query: ")
    b._async_client = _client_capturing(inputs, [[1.0, 2.0]])

    await b.embed_query(["find the parser"])

    sent = inputs[0]
    if isinstance(sent, list):
        sent = sent[0]
    assert sent == "Represent this query: find the parser"


@pytest.mark.asyncio
async def test_embed_query_no_prefix_when_instruction_empty():
    inputs: list = []
    b = _backend(query_instruction="")
    b._async_client = _client_capturing(inputs, [[1.0, 2.0]])

    await b.embed_query(["find the parser"])

    sent = inputs[0]
    if isinstance(sent, list):
        sent = sent[0]
    assert sent == "find the parser"


@pytest.mark.asyncio
async def test_embed_batch_docs_never_prefixed():
    inputs: list = []
    b = _backend(query_instruction="Represent this query: ")
    b._async_client = _client_capturing(inputs, [[1.0, 2.0]])

    await b.embed_batch(["def foo(): pass"])

    sent = inputs[0]
    if isinstance(sent, list):
        sent = sent[0]
    assert sent == "def foo(): pass"


@pytest.mark.asyncio
async def test_normalize_output_l2_normalizes():
    b = _backend(normalize_output=True)
    b._async_client = _client_capturing([], [[3.0, 4.0]])

    result = await b.embed_batch(["x"])

    assert result[0] == pytest.approx([0.6, 0.8])


@pytest.mark.asyncio
async def test_no_normalize_by_default():
    b = _backend(normalize_output=False)
    b._async_client = _client_capturing([], [[3.0, 4.0]])

    result = await b.embed_batch(["x"])

    assert result[0] == [3.0, 4.0]


def test_l2_normalize_handles_zero_vector():
    assert TeiDenseBackend._l2_normalize([0.0, 0.0]) == [0.0, 0.0]


@pytest.mark.asyncio
async def test_embedder_query_path_applies_instruction_prefix():
    """R1: real Embedder.embed_query must route dense through embed_query so the
    inf-retriever query prefix is applied during actual scoring, not just when
    TeiDenseBackend.embed_query is called directly."""
    from codebase_indexer.indexer.embedder import Embedder

    inputs: list = []
    dense = _backend(query_instruction="Represent this query: ")
    dense._async_client = _client_capturing(inputs, [[1.0, 2.0]])

    sparse = MagicMock()
    sparse.embed_batch = AsyncMock(return_value=[None])

    embedder = Embedder(
        dense_backend=dense,
        sparse_backend=sparse,
        dense_embed_vector_size=2,
        hybrid=False,
    )

    await embedder.embed_query("find the parser")

    sent = inputs[0]
    if isinstance(sent, list):
        sent = sent[0]
    assert sent == "Represent this query: find the parser"


@pytest.mark.asyncio
async def test_embedder_queries_path_applies_instruction_prefix():
    """R1: batched Embedder.embed_queries must also apply the query prefix."""
    from codebase_indexer.indexer.embedder import Embedder

    inputs: list = []
    dense = _backend(query_instruction="Represent this query: ")
    dense._async_client = _client_capturing(inputs, [[1.0, 2.0]])

    sparse = MagicMock()
    sparse.embed_batch = AsyncMock(return_value=[None])

    embedder = Embedder(
        dense_backend=dense,
        sparse_backend=sparse,
        dense_embed_vector_size=2,
        hybrid=False,
    )

    await embedder.embed_queries(["find the parser"])

    sent = inputs[0]
    if isinstance(sent, list):
        sent = sent[0]
    assert sent == "Represent this query: find the parser"
