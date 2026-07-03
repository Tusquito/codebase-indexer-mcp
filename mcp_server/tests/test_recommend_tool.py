"""Unit tests for recommend_code MCP tool handler."""

from unittest.mock import AsyncMock, MagicMock

import pytest

from codebase_indexer.config import Settings
from codebase_indexer.context import AppContext
from codebase_indexer.storage.qdrant import QdrantStorage, SearchResult
from codebase_indexer.tools.recommend import register_recommend_tool


def _make_ctx(
    *,
    recommend_max_examples: int = 10,
) -> AppContext:
    settings = Settings(recommend_max_examples=recommend_max_examples)
    storage = MagicMock(spec=QdrantStorage)
    storage.chunk_id_to_point_id = QdrantStorage.chunk_id_to_point_id
    storage.verify_chunk_ids_exist = AsyncMock()
    storage.recommend = AsyncMock(return_value=[])
    embedder = MagicMock()
    embedder.embed_batch_dense = AsyncMock(return_value=[[0.1] * 768])
    return AppContext(
        settings=settings,
        storage=storage,
        embedder=embedder,
        job_tracker=MagicMock(),
        url_extractors=MagicMock(),
    )


def _get_handler(mcp_mock: MagicMock):
    return mcp_mock._handler


@pytest.fixture
def recommend_handler():
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
    register_recommend_tool(mcp, ctx)
    handler = captured["handler"]
    handler._ctx = ctx  # attach for test access
    return handler


@pytest.mark.asyncio
async def test_recommend_code_requires_positive_example(recommend_handler):
    with pytest.raises(ValueError, match="At least one positive"):
        await recommend_handler(collection="proj")


@pytest.mark.asyncio
async def test_recommend_code_caps_limit_at_20(recommend_handler):
    ctx = recommend_handler._ctx
    ctx.storage.recommend = AsyncMock(return_value=[])
    await recommend_handler(
        collection="proj",
        positive_chunk_ids=["a.py:1"],
        limit=50,
    )
    ctx.storage.recommend.assert_awaited_once()
    assert ctx.storage.recommend.await_args.kwargs["limit"] == 20


@pytest.mark.asyncio
async def test_recommend_code_enforces_max_examples():
    mcp = MagicMock()
    captured: dict = {}

    def fake_tool(**kwargs):
        def decorator(fn):
            captured["handler"] = fn
            return fn

        return decorator

    mcp.tool = fake_tool
    ctx = _make_ctx(recommend_max_examples=2)
    register_recommend_tool(mcp, ctx)
    handler = captured["handler"]

    with pytest.raises(ValueError, match="RECOMMEND_MAX_EXAMPLES"):
        await handler(
            collection="proj",
            positive_chunk_ids=["a.py:1", "b.py:1"],
            negative_chunk_ids=["c.py:1"],
        )


@pytest.mark.asyncio
async def test_recommend_code_resolves_chunk_ids_and_embeds_queries(recommend_handler):
    ctx = recommend_handler._ctx
    ctx.storage.recommend = AsyncMock(
        return_value=[
            SearchResult(
                chunk_id="src/h.py:1",
                score=0.9,
                rel_path="src/h.py",
                language="python",
                start_line=1,
                end_line=10,
                symbol_name="handler",
                symbol_type="function",
                content="def handler(): ...",
                collection="proj",
            )
        ]
    )
    ctx.embedder.embed_batch_dense = AsyncMock(
        return_value=[[0.2] * 768, [0.3] * 768]
    )

    result = await recommend_handler(
        collection="proj",
        positive_chunk_ids=["pos.py:1"],
        positive_query="handler pattern",
        negative_chunk_ids=["neg.py:1"],
        negative_query="test utilities",
        limit=5,
        path_glob="src/*.py",
    )

    ctx.storage.verify_chunk_ids_exist.assert_awaited_once_with(
        "proj", ["pos.py:1", "neg.py:1"]
    )
    ctx.embedder.embed_batch_dense.assert_awaited_once_with(
        ["handler pattern", "test utilities"]
    )
    recommend_kwargs = ctx.storage.recommend.await_args.kwargs
    assert recommend_kwargs["collection"] == "proj"
    assert recommend_kwargs["limit"] == 5
    assert recommend_kwargs["path_glob"] == "src/*.py"
    assert len(recommend_kwargs["positive"]) == 2
    assert len(recommend_kwargs["negative"]) == 2
    assert result["collection"] == "proj"
    assert result["positive_examples"] == 2
    assert result["negative_examples"] == 2
    assert len(result["results"]) == 1
    assert result["results"][0]["rel_path"] == "src/h.py"


@pytest.mark.asyncio
async def test_recommend_code_truncates_content(recommend_handler):
    ctx = recommend_handler._ctx
    long_content = "x" * 100
    ctx.storage.recommend = AsyncMock(
        return_value=[
            SearchResult(
                chunk_id="a.py:1",
                score=0.5,
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

    result = await recommend_handler(
        collection="proj",
        positive_query="example",
        max_content_chars=10,
    )

    assert result["results"][0]["content"] == "x" * 10
    assert result["results"][0]["content_truncated"] is True
