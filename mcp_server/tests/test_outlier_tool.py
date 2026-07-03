"""Unit tests for find_outlier_chunks MCP tool handler."""

from unittest.mock import AsyncMock, MagicMock

import pytest

from codebase_indexer.config import Settings
from codebase_indexer.context import AppContext
from codebase_indexer.storage.qdrant import QdrantStorage, SearchResult
from codebase_indexer.tools.outliers import register_find_outlier_chunks_tool


def _make_ctx() -> AppContext:
    settings = Settings()
    storage = MagicMock(spec=QdrantStorage)
    storage.chunk_id_to_point_id = QdrantStorage.chunk_id_to_point_id
    storage.verify_chunk_ids_exist = AsyncMock()
    storage.find_outlier_chunks = AsyncMock(return_value=[])
    return AppContext(
        settings=settings,
        storage=storage,
        embedder=MagicMock(),
        job_tracker=MagicMock(),
        url_extractors=MagicMock(),
    )


@pytest.fixture
def outlier_handler():
    mcp = MagicMock()
    captured: dict = {}

    def fake_tool(**kwargs):
        def decorator(fn):
            captured["handler"] = fn
            mcp._handler = fn
            return fn

        return decorator

    mcp.tool = fake_tool
    ctx = _make_ctx()
    register_find_outlier_chunks_tool(mcp, ctx)
    handler = captured["handler"]
    handler._ctx = ctx
    return handler


@pytest.mark.asyncio
async def test_find_outlier_chunks_requires_collection(outlier_handler):
    ctx = outlier_handler._ctx
    ctx.storage.find_outlier_chunks = AsyncMock(return_value=[])
    result = await outlier_handler(collection="proj")
    assert result["collection"] == "proj"
    ctx.storage.find_outlier_chunks.assert_awaited_once()


@pytest.mark.asyncio
async def test_find_outlier_chunks_caps_limit_at_20(outlier_handler):
    ctx = outlier_handler._ctx
    ctx.storage.find_outlier_chunks = AsyncMock(return_value=[])
    await outlier_handler(collection="proj", limit=50)
    assert ctx.storage.find_outlier_chunks.await_args.kwargs["limit"] == 20


@pytest.mark.asyncio
async def test_find_outlier_chunks_unknown_context_ids_fail_fast(outlier_handler):
    ctx = outlier_handler._ctx
    ctx.storage.verify_chunk_ids_exist = AsyncMock(
        side_effect=ValueError("Unknown chunk_id(s) in collection 'proj': ghost.py:1")
    )
    with pytest.raises(ValueError, match="ghost.py:1"):
        await outlier_handler(
            collection="proj",
            context_chunk_ids=["ghost.py:1"],
        )


@pytest.mark.asyncio
async def test_find_outlier_chunks_result_shape(outlier_handler):
    ctx = outlier_handler._ctx
    ctx.storage.find_outlier_chunks = AsyncMock(
        return_value=[
            SearchResult(
                chunk_id="src/out.py:1",
                score=0.12,
                rel_path="src/out.py",
                language="python",
                start_line=1,
                end_line=10,
                symbol_name="orphan",
                symbol_type="function",
                content="def orphan(): pass",
                collection="proj",
            )
        ]
    )

    result = await outlier_handler(
        collection="proj",
        context_chunk_ids=["src/main.py:1"],
        max_similarity=0.5,
    )

    assert result["collection"] == "proj"
    assert result["context_examples"] == 1
    assert result["max_similarity"] == 0.5
    assert len(result["results"]) == 1
    item = result["results"][0]
    assert item["chunk_id"] == "src/out.py:1"
    assert item["score"] == 0.12
    assert item["similarity_to_context"] == 0.12
    assert item["rel_path"] == "src/out.py"


@pytest.mark.asyncio
async def test_find_outlier_chunks_truncates_content(outlier_handler):
    ctx = outlier_handler._ctx
    long_content = "x" * 100
    ctx.storage.find_outlier_chunks = AsyncMock(
        return_value=[
            SearchResult(
                chunk_id="a.py:1",
                score=0.1,
                rel_path="a.py",
                language="python",
                start_line=1,
                end_line=2,
                symbol_name=None,
                symbol_type="other",
                content=long_content,
                collection="proj",
            )
        ]
    )

    result = await outlier_handler(
        collection="proj",
        max_content_chars=10,
    )

    assert result["results"][0]["content"] == "x" * 10
    assert result["results"][0]["content_truncated"] is True
