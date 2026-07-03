"""Unit tests for QdrantStorage recommendation helpers (no live Qdrant)."""

import uuid
from unittest.mock import AsyncMock, MagicMock

import pytest
from qdrant_client.models import RecommendQuery, RecommendStrategy

from codebase_indexer.config import Settings
from codebase_indexer.storage.qdrant import QdrantStorage


def test_chunk_id_to_point_id_matches_build_point():
    chunk_id = "src/foo.py:10"
    expected = str(uuid.uuid5(uuid.NAMESPACE_URL, chunk_id))
    assert QdrantStorage.chunk_id_to_point_id(chunk_id) == expected


@pytest.mark.asyncio
async def test_verify_chunk_ids_exist_passes_when_all_found():
    storage = QdrantStorage(Settings())
    chunk_ids = ["a.py:1", "b.py:2"]
    point_ids = [storage.chunk_id_to_point_id(cid) for cid in chunk_ids]

    record_a = MagicMock()
    record_a.id = point_ids[0]
    record_b = MagicMock()
    record_b.id = point_ids[1]

    mock_client = MagicMock()
    mock_client.retrieve = AsyncMock(return_value=[record_a, record_b])
    storage._get_client = AsyncMock(return_value=mock_client)

    await storage.verify_chunk_ids_exist("coll", chunk_ids)
    mock_client.retrieve.assert_awaited_once_with(
        collection_name="coll",
        ids=point_ids,
        with_payload=False,
        with_vectors=False,
    )


@pytest.mark.asyncio
async def test_verify_chunk_ids_exist_raises_for_unknown():
    storage = QdrantStorage(Settings())
    chunk_ids = ["known.py:1", "missing.py:9"]
    point_ids = [storage.chunk_id_to_point_id(cid) for cid in chunk_ids]

    record = MagicMock()
    record.id = point_ids[0]

    mock_client = MagicMock()
    mock_client.retrieve = AsyncMock(return_value=[record])
    storage._get_client = AsyncMock(return_value=mock_client)

    with pytest.raises(ValueError, match="missing.py:9"):
        await storage.verify_chunk_ids_exist("coll", chunk_ids)


@pytest.mark.asyncio
async def test_verify_chunk_ids_exist_raises_lists_all_unknown():
    storage = QdrantStorage(Settings())
    chunk_ids = ["x.py:1", "y.py:2"]

    mock_client = MagicMock()
    mock_client.retrieve = AsyncMock(return_value=[])
    storage._get_client = AsyncMock(return_value=mock_client)

    with pytest.raises(ValueError, match="x.py:1") as exc:
        await storage.verify_chunk_ids_exist("coll", chunk_ids)
    assert "y.py:2" in str(exc.value)


@pytest.mark.asyncio
async def test_recommend_calls_query_points_with_recommend_query(monkeypatch):
    storage = QdrantStorage(Settings())
    captured: dict = {}

    async def fake_query_points(**kwargs):
        captured.update(kwargs)
        point = MagicMock()
        point.score = 0.88
        point.payload = {
            "chunk_id": "src/a.py:1",
            "rel_path": "src/a.py",
            "language": "python",
            "start_line": 1,
            "end_line": 5,
            "symbol_name": "foo",
            "symbol_type": "function",
            "content": "def foo(): pass",
        }
        result = MagicMock()
        result.points = [point]
        return result

    mock_client = MagicMock()
    mock_client.query_points = AsyncMock(side_effect=fake_query_points)
    monkeypatch.setattr(storage, "_get_client", AsyncMock(return_value=mock_client))

    pos_id = storage.chunk_id_to_point_id("src/a.py:1")
    neg_vec = [0.2] * 768
    results = await storage.recommend(
        collection="my_coll",
        positive=[pos_id],
        negative=[neg_vec],
        limit=5,
        language="python",
    )

    assert len(results) == 1
    assert results[0].chunk_id == "src/a.py:1"
    assert results[0].collection == "my_coll"
    assert captured["using"] == "dense"
    assert captured["limit"] == 5
    query = captured["query"]
    assert isinstance(query, RecommendQuery)
    assert query.recommend.strategy == RecommendStrategy.AVERAGE_VECTOR
    assert query.recommend.positive == [pos_id]
    assert query.recommend.negative == [neg_vec]
    assert captured["query_filter"] is not None


@pytest.mark.asyncio
async def test_recommend_path_glob_over_fetches_and_post_filters(monkeypatch):
    storage = QdrantStorage(Settings())

    async def fake_query_points(**kwargs):
        assert kwargs["limit"] == 9  # limit 3 * 3
        points = []
        for rel_path in ("src/a.py", "tests/test_a.py", "src/b.py"):
            point = MagicMock()
            point.score = 0.5
            point.payload = {
                "chunk_id": f"{rel_path}:1",
                "rel_path": rel_path,
                "language": "python",
                "start_line": 1,
                "end_line": 2,
                "symbol_name": None,
                "symbol_type": "other",
                "content": rel_path,
            }
            points.append(point)
        result = MagicMock()
        result.points = points
        return result

    mock_client = MagicMock()
    mock_client.query_points = AsyncMock(side_effect=fake_query_points)
    monkeypatch.setattr(storage, "_get_client", AsyncMock(return_value=mock_client))

    results = await storage.recommend(
        collection="coll",
        positive=[[0.1] * 768],
        limit=3,
        path_glob="src/*.py",
    )

    assert len(results) == 2
    assert all(r.rel_path.startswith("src/") for r in results)
