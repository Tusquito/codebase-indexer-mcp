"""Unit tests for graph_call_sites payload omission and collection metadata."""

from unittest.mock import AsyncMock, MagicMock

import pytest

from codebase_indexer.config import Settings
from codebase_indexer.indexer.chunker import Chunk
from codebase_indexer.indexer.embedder import EmbeddedChunk
from codebase_indexer.storage.qdrant import QdrantStorage


def _settings(**overrides) -> Settings:
    base = dict(
        dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=768,
        sparse_threads=2,
    )
    base.update(overrides)
    return Settings(**base)


def _embedded_chunk(callees: list[str] | None = None) -> EmbeddedChunk:
    chunk = Chunk(
        chunk_id="cid-1",
        content="x.foo()",
        rel_path="a.py",
        language="python",
        start_line=1,
        end_line=1,
        symbol_name="x",
        symbol_type="function",
        file_sha256="abc",
        callees=callees or ["foo", "bar.baz"],
    )
    return EmbeddedChunk(chunk=chunk, dense_vector=[0.1, 0.2], sparse_vector=None)


def test_build_point_includes_callees_by_default():
    storage = QdrantStorage(_settings())
    point = storage._build_point(_embedded_chunk())
    assert point.payload["callees"] == ["foo", "bar.baz"]


def test_build_point_omits_callees_when_flag_set():
    storage = QdrantStorage(_settings())
    point = storage._build_point(_embedded_chunk(), omit_callees=True)
    assert "callees" not in point.payload


@pytest.mark.asyncio
async def test_set_collection_graph_call_sites_updates_metadata():
    storage = QdrantStorage(_settings())
    client = AsyncMock()
    storage._client = client

    await storage.set_collection_graph_call_sites("demo", True)

    client.update_collection.assert_awaited_once()
    call_kwargs = client.update_collection.await_args.kwargs
    assert call_kwargs["collection_name"] == "demo"
    assert call_kwargs["metadata"]["graph_call_sites"] is True


@pytest.mark.asyncio
async def test_collection_has_graph_call_sites_reads_metadata():
    storage = QdrantStorage(_settings())
    client = AsyncMock()
    info = MagicMock()
    info.config.metadata = {"graph_call_sites": True}
    client.get_collection = AsyncMock(return_value=info)
    storage._client = client

    assert await storage.collection_has_graph_call_sites("demo") is True


@pytest.mark.asyncio
async def test_collection_has_graph_call_sites_false_when_missing():
    storage = QdrantStorage(_settings())
    client = AsyncMock()
    info = MagicMock()
    info.config.metadata = {}
    client.get_collection = AsyncMock(return_value=info)
    storage._client = client

    assert await storage.collection_has_graph_call_sites("demo") is False


def test_build_point_includes_graph_node_ids():
    storage = QdrantStorage(_settings())
    point = storage._build_point(
        _embedded_chunk(),
        omit_callees=True,
        graph_node_ids=["demo:a.py::x", "demo::callee::foo"],
    )
    assert point.payload["graph_node_ids"] == ["demo:a.py::x", "demo::callee::foo"]


def test_build_point_omits_graph_node_ids_when_none():
    storage = QdrantStorage(_settings())
    point = storage._build_point(_embedded_chunk())
    assert "graph_node_ids" not in point.payload


@pytest.mark.asyncio
async def test_upsert_chunks_attaches_per_chunk_graph_node_ids():
    storage = QdrantStorage(_settings())
    client = AsyncMock()
    storage._client = client

    ec = _embedded_chunk()
    await storage.upsert_chunks(
        "demo",
        [ec],
        omit_callees=True,
        graph_node_ids_by_chunk={ec.chunk.chunk_id: ["demo:a.py::x"]},
    )

    client.upsert.assert_awaited_once()
    points = client.upsert.await_args.kwargs["points"]
    assert points[0].payload["graph_node_ids"] == ["demo:a.py::x"]


@pytest.mark.asyncio
async def test_set_collection_graph_enabled_updates_metadata():
    storage = QdrantStorage(_settings())
    client = AsyncMock()
    storage._client = client

    await storage.set_collection_graph_enabled("demo", True)

    client.update_collection.assert_awaited_once()
    call_kwargs = client.update_collection.await_args.kwargs
    assert call_kwargs["collection_name"] == "demo"
    assert call_kwargs["metadata"]["graph_enabled"] is True


@pytest.mark.asyncio
async def test_collection_has_graph_enabled_reads_metadata():
    storage = QdrantStorage(_settings())
    client = AsyncMock()
    info = MagicMock()
    info.config.metadata = {"graph_enabled": True}
    client.get_collection = AsyncMock(return_value=info)
    storage._client = client

    assert await storage.collection_has_graph_enabled("demo") is True


@pytest.mark.asyncio
async def test_collection_has_graph_enabled_false_when_missing():
    storage = QdrantStorage(_settings())
    client = AsyncMock()
    info = MagicMock()
    info.config.metadata = {}
    client.get_collection = AsyncMock(return_value=info)
    storage._client = client

    assert await storage.collection_has_graph_enabled("demo") is False
