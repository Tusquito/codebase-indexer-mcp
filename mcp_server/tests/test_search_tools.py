"""Unit tests for search_codebase and search_symbols rerank override wiring."""

from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock

import pytest
from fastmcp import FastMCP

from codebase_indexer.tools.search import register_search_tool
from codebase_indexer.tools.symbols import register_search_symbols_tool


def _make_ctx(*, collection: str = "proj-a"):
    storage = AsyncMock()
    storage.search = AsyncMock(return_value=[])
    embedder = MagicMock()
    embedder.embed_query = AsyncMock(return_value=([0.1], None, None))
    settings = SimpleNamespace(qdrant_collection=collection)
    return SimpleNamespace(
        storage=storage,
        embedder=embedder,
        settings=settings,
        url_extractors=MagicMock(),
    )


@pytest.mark.asyncio
async def test_search_codebase_rerank_false_passes_to_embedder():
    ctx = _make_ctx()
    mcp = FastMCP("test")
    register_search_tool(mcp, ctx)
    search_codebase = (await mcp.get_tool("search_codebase")).fn

    await search_codebase(query="auth handler", rerank=False)

    ctx.embedder.embed_query.assert_awaited_once_with("auth handler", rerank=False)
    assert ctx.storage.search.await_args.kwargs["colbert_vector"] is None


@pytest.mark.asyncio
async def test_search_symbols_rerank_false_passes_to_embedder():
    ctx = _make_ctx()
    mcp = FastMCP("test")
    register_search_symbols_tool(mcp, ctx)
    search_symbols = (await mcp.get_tool("search_symbols")).fn

    await search_symbols(query="LoginService", rerank=False)

    ctx.embedder.embed_query.assert_awaited_once_with("LoginService", rerank=False)
    assert ctx.storage.search.await_args.kwargs["colbert_vector"] is None
