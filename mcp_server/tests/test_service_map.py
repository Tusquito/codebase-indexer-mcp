"""Unit tests for map_service_dependencies ColBERT rerank wiring."""

from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock

import pytest
from fastmcp import FastMCP

from codebase_indexer.indexer.embedder import SparseVector
from codebase_indexer.tools.cross_references import UrlExtractors
from codebase_indexer.tools.service_map import _DISCOVERY_QUERIES, register_service_map_tool

_DENSE = [0.1]
_SPARSE = SparseVector(indices=[1], values=[1.0])
_COLBERT = [[0.2, 0.3], [0.4, 0.5]]


@pytest.mark.asyncio
async def test_map_service_dependencies_passes_colbert_vector_on_discovery_search():
    query_vectors = [(_DENSE, _SPARSE, _COLBERT) for _ in _DISCOVERY_QUERIES]

    storage = AsyncMock()
    storage.search = AsyncMock(return_value=[])

    embedder = MagicMock()
    embedder.embed_queries = AsyncMock(return_value=query_vectors)

    settings = SimpleNamespace(service_discovery_extra_query_list=[])

    ctx = SimpleNamespace(
        storage=storage,
        embedder=embedder,
        url_extractors=UrlExtractors(),
        settings=settings,
    )

    mcp = FastMCP("test")
    register_service_map_tool(mcp, ctx)
    map_service_dependencies = (await mcp.get_tool("map_service_dependencies")).fn

    await map_service_dependencies(collections=["svc-a", "svc-b"])

    assert storage.search.await_count == len(_DISCOVERY_QUERIES)
    for call in storage.search.await_args_list:
        assert call.kwargs["colbert_vector"] == _COLBERT


@pytest.mark.asyncio
async def test_map_service_dependencies_rerank_false_skips_colbert_embed():
    query_vectors = [(_DENSE, _SPARSE, None) for _ in _DISCOVERY_QUERIES]

    storage = AsyncMock()
    storage.search = AsyncMock(return_value=[])

    embedder = MagicMock()
    embedder.embed_queries = AsyncMock(return_value=query_vectors)

    settings = SimpleNamespace(service_discovery_extra_query_list=[])

    ctx = SimpleNamespace(
        storage=storage,
        embedder=embedder,
        url_extractors=UrlExtractors(),
        settings=settings,
    )

    mcp = FastMCP("test")
    register_service_map_tool(mcp, ctx)
    map_service_dependencies = (await mcp.get_tool("map_service_dependencies")).fn

    await map_service_dependencies(collections=["svc-a", "svc-b"], rerank=False)

    embedder.embed_queries.assert_awaited_once()
    assert embedder.embed_queries.await_args.kwargs["rerank"] is False
    for call in storage.search.await_args_list:
        assert call.kwargs["colbert_vector"] is None
