"""Tests for get_chunk collection resolution."""

from unittest.mock import AsyncMock, call

import pytest

from codebase_indexer.config import Settings
from codebase_indexer.storage.qdrant import CollectionStats, QdrantStorage


@pytest.mark.asyncio
async def test_find_chunk_by_id_searches_all_collections_when_unspecified():
    storage = QdrantStorage(Settings(qdrant_url="http://localhost:6333"))
    storage.list_collection_stats = AsyncMock(
        return_value=[
            CollectionStats("alpha", 1, 0.0, "model", "bm25", "ollama", True),
            CollectionStats("beta", 2, 0.0, "model", "bm25", "ollama", True),
        ]
    )
    storage.get_chunk_by_id = AsyncMock(side_effect=[None, {"chunk_id": "abc", "content": "x"}])

    result = await storage.find_chunk_by_id("abc")

    assert result == {"chunk_id": "abc", "content": "x"}
    storage.get_chunk_by_id.assert_has_awaits(
        [call("alpha", "abc"), call("beta", "abc")]
    )


@pytest.mark.asyncio
async def test_find_chunk_by_id_uses_explicit_collection():
    storage = QdrantStorage(Settings(qdrant_url="http://localhost:6333"))
    storage.get_chunk_by_id = AsyncMock(return_value={"chunk_id": "abc"})
    storage.list_collection_stats = AsyncMock()

    result = await storage.find_chunk_by_id("abc", collection="crm_ods_cicd")

    assert result == {"chunk_id": "abc"}
    storage.get_chunk_by_id.assert_awaited_once_with("crm_ods_cicd", "abc")
    storage.list_collection_stats.assert_not_awaited()
