"""Unit tests for the expand_search_context MCP tool (ADR 0002 Phase 3).

Uses a stub graph_storage and a monkeypatched run_search — no live Neo4j.
"""

from unittest.mock import AsyncMock, MagicMock

import pytest

import codebase_indexer.tools.graph_search as graph_search
from codebase_indexer.config import Settings
from codebase_indexer.context import AppContext
from codebase_indexer.storage.neo4j import GraphEdge, GraphExpansion, GraphNode
from codebase_indexer.storage.qdrant import QdrantStorage, SearchResult
from codebase_indexer.tools.graph_search import register_expand_search_context_tool


def _seed_result(chunk_id: str = "proj/a.py:1", collection: str = "proj") -> SearchResult:
    return SearchResult(
        chunk_id=chunk_id,
        score=0.9,
        rel_path="a.py",
        language="python",
        start_line=1,
        end_line=10,
        symbol_name="handler",
        symbol_type="function",
        content="def handler(): ...",
        collection=collection,
    )


def _make_ctx(*, graph_enabled: bool = True, graph_max_hops: int = 2) -> AppContext:
    settings = Settings(
        graph_enabled=graph_enabled,
        neo4j_password="pw" if graph_enabled else "",
        graph_max_hops=graph_max_hops,
        graph_max_nodes=200,
    )
    storage = MagicMock(spec=QdrantStorage)
    storage.get_chunk_by_id = AsyncMock(return_value=None)
    storage.find_chunk_by_id = AsyncMock(return_value=None)
    graph_storage = MagicMock()
    graph_storage.enabled = graph_enabled
    graph_storage.expand_subgraph = AsyncMock(return_value=GraphExpansion())
    return AppContext(
        settings=settings,
        storage=storage,
        embedder=MagicMock(),
        job_tracker=MagicMock(),
        url_extractors=MagicMock(),
        graph_storage=graph_storage if graph_enabled else None,
    )


def _register(ctx: AppContext):
    mcp = MagicMock()
    captured: dict = {}

    def fake_tool(**kwargs):
        def decorator(fn):
            captured["handler"] = fn
            return fn

        return decorator

    mcp.tool = fake_tool
    register_expand_search_context_tool(mcp, ctx)
    handler = captured["handler"]
    handler._ctx = ctx
    return handler


@pytest.fixture(autouse=True)
def _patch_run_search(monkeypatch):
    mock = AsyncMock(return_value=[_seed_result()])
    monkeypatch.setattr(graph_search, "run_search", mock)
    return mock


@pytest.mark.asyncio
async def test_collects_seed_chunk_ids_only(_patch_run_search):
    ctx = _make_ctx()
    handler = _register(ctx)

    await handler(query="find handler", collection="proj")

    ctx.graph_storage.expand_subgraph.assert_awaited_once()
    kwargs = ctx.graph_storage.expand_subgraph.await_args.kwargs
    assert kwargs["chunk_ids"] == ["proj/a.py:1"]


@pytest.mark.asyncio
async def test_hop_clamped_to_graph_max_hops(_patch_run_search):
    ctx = _make_ctx(graph_max_hops=2)
    handler = _register(ctx)

    await handler(query="q", collection="proj", graph_hops=99)

    kwargs = ctx.graph_storage.expand_subgraph.await_args.kwargs
    assert kwargs["max_hops"] == 2


@pytest.mark.asyncio
async def test_hop_defaults_to_graph_max_hops(_patch_run_search):
    ctx = _make_ctx(graph_max_hops=2)
    handler = _register(ctx)

    result = await handler(query="q", collection="proj")

    assert ctx.graph_storage.expand_subgraph.await_args.kwargs["max_hops"] == 2
    assert result["graph_hops"] == 2


@pytest.mark.asyncio
async def test_graph_context_shape(_patch_run_search):
    ctx = _make_ctx()
    ctx.graph_storage.expand_subgraph = AsyncMock(
        return_value=GraphExpansion(
            nodes=[GraphNode(labels=["Chunk"], key="proj/b.py:2", props={"chunk_id": "proj/b.py:2"})],
            edges=[GraphEdge(type="CALLS", from_key="proj/a.py:1", to_key="sym.foo")],
            related_chunk_ids=["proj/b.py:2"],
            related_chunk_collections={"proj/b.py:2": "proj"},
        )
    )
    ctx.storage.get_chunk_by_id = AsyncMock(
        return_value={
            "content": "def b(): ...",
            "rel_path": "b.py",
            "symbol_name": "b",
            "symbol_type": "function",
            "start_line": 2,
            "end_line": 5,
            "language": "python",
            "collection": "proj",
        }
    )
    handler = _register(ctx)

    result = await handler(query="q", collection="proj")

    assert set(result) >= {"nodes", "edges", "related_chunks", "seeds"}
    assert result["nodes"][0]["key"] == "proj/b.py:2"
    assert result["edges"][0]["type"] == "CALLS"
    assert result["related_chunks"][0]["chunk_id"] == "proj/b.py:2"
    assert result["seeds"][0]["chunk_id"] == "proj/a.py:1"
    ctx.storage.get_chunk_by_id.assert_awaited_once_with("proj", "proj/b.py:2")


@pytest.mark.asyncio
async def test_disabled_graph_returns_guarded_context(_patch_run_search):
    ctx = _make_ctx()
    # Simulate runtime-disabled graph storage even though registered.
    ctx.graph_storage.enabled = False
    handler = _register(ctx)

    result = await handler(query="q", collection="proj")

    assert result["nodes"] == []
    assert result["edges"] == []
    assert result["related_chunks"] == []
    assert result["seeds"][0]["chunk_id"] == "proj/a.py:1"
    ctx.graph_storage.expand_subgraph.assert_not_awaited()


@pytest.mark.asyncio
async def test_top_k_capped_at_20(_patch_run_search):
    ctx = _make_ctx()
    handler = _register(ctx)

    await handler(query="q", collection="proj", top_k=100)

    # run_search is called with the capped top_k (positional arg index 4).
    args = _patch_run_search.await_args.args
    assert 20 in args
