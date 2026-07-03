"""Unit tests for QdrantStorage outlier helpers (no live Qdrant)."""

import uuid
from unittest.mock import AsyncMock, MagicMock

import pytest
from qdrant_client.models import RecommendQuery, RecommendStrategy

from codebase_indexer.config import Settings
from codebase_indexer.storage.qdrant import QdrantStorage


def test_compute_centroid_averages_vectors():
    vectors = [
        [1.0, 0.0, 0.0],
        [0.0, 1.0, 0.0],
        [0.0, 0.0, 1.0],
    ]
    centroid = QdrantStorage._compute_centroid(vectors)
    assert centroid == pytest.approx([1 / 3, 1 / 3, 1 / 3])


def test_compute_centroid_empty_returns_empty():
    assert QdrantStorage._compute_centroid([]) == []


def test_cosine_similarity_orthogonal_is_zero():
    a = [1.0, 0.0]
    b = [0.0, 1.0]
    assert QdrantStorage._cosine_similarity(a, b) == pytest.approx(0.0)


def test_cosine_similarity_identical_is_one():
    a = [1.0, 2.0, 3.0]
    assert QdrantStorage._cosine_similarity(a, a) == pytest.approx(1.0)


@pytest.mark.asyncio
async def test_find_outlier_chunks_uses_best_score_negative_only(monkeypatch):
    storage = QdrantStorage(Settings())
    captured: dict = {}

    cluster_vec = [1.0 if i < 10 else 0.01 for i in range(768)]
    outlier_vec = [0.01 if i < 10 else 1.0 for i in range(768)]
    context_cid = "src/cluster.py:1"
    outlier_cid = "src/outlier.py:1"
    context_pid = storage.chunk_id_to_point_id(context_cid)

    async def fake_sample(*args, **kwargs):
        return [(context_cid, context_pid, cluster_vec)]

    async def fake_query_points(**kwargs):
        captured.update(kwargs)
        point = MagicMock()
        point.id = storage.chunk_id_to_point_id(outlier_cid)
        point.vector = {"dense": outlier_vec}
        point.payload = {
            "chunk_id": outlier_cid,
            "rel_path": "src/outlier.py",
            "language": "python",
            "start_line": 1,
            "end_line": 5,
            "symbol_name": "outlier_fn",
            "symbol_type": "function",
            "content": "def outlier_fn(): pass",
        }
        result = MagicMock()
        result.points = [point]
        return result

    mock_client = MagicMock()
    mock_client.query_points = AsyncMock(side_effect=fake_query_points)
    monkeypatch.setattr(
        storage, "sample_context_dense_vectors", AsyncMock(side_effect=fake_sample)
    )
    monkeypatch.setattr(storage, "_get_client", AsyncMock(return_value=mock_client))

    results = await storage.find_outlier_chunks(
        collection="coll",
        context_chunk_ids=[context_cid],
        limit=5,
        max_similarity=0.55,
    )

    assert len(results) == 1
    assert results[0].chunk_id == outlier_cid
    assert results[0].score < 0.55
    query = captured["query"]
    assert isinstance(query, RecommendQuery)
    assert query.recommend.strategy == RecommendStrategy.BEST_SCORE
    assert query.recommend.positive == []
    assert query.recommend.negative == [context_pid]
    assert captured["using"] == "dense"


@pytest.mark.asyncio
async def test_find_outlier_chunks_filters_above_max_similarity(monkeypatch):
    storage = QdrantStorage(Settings())
    cluster_vec = [1.0 if i < 10 else 0.01 for i in range(768)]
    similar_vec = [0.99 if i < 10 else 0.01 for i in range(768)]
    distant_vec = [0.01 if i < 10 else 1.0 for i in range(768)]
    context_cid = "src/a.py:1"
    context_pid = storage.chunk_id_to_point_id(context_cid)

    async def fake_sample(*args, **kwargs):
        return [(context_cid, context_pid, cluster_vec)]

    async def fake_query_points(**kwargs):
        points = []
        for cid, vec in (
            ("src/similar.py:1", similar_vec),
            ("src/distant.py:1", distant_vec),
        ):
            point = MagicMock()
            point.id = storage.chunk_id_to_point_id(cid)
            point.vector = {"dense": vec}
            point.payload = {
                "chunk_id": cid,
                "rel_path": cid.split(":")[0],
                "language": "python",
                "start_line": 1,
                "end_line": 2,
                "symbol_name": None,
                "symbol_type": "other",
                "content": cid,
            }
            points.append(point)
        result = MagicMock()
        result.points = points
        return result

    mock_client = MagicMock()
    mock_client.query_points = AsyncMock(side_effect=fake_query_points)
    monkeypatch.setattr(
        storage, "sample_context_dense_vectors", AsyncMock(side_effect=fake_sample)
    )
    monkeypatch.setattr(storage, "_get_client", AsyncMock(return_value=mock_client))

    results = await storage.find_outlier_chunks(
        collection="coll",
        context_chunk_ids=[context_cid],
        limit=10,
        max_similarity=0.55,
    )

    assert len(results) == 1
    assert results[0].chunk_id == "src/distant.py:1"


@pytest.mark.asyncio
async def test_find_outlier_chunks_path_glob_over_fetches(monkeypatch):
    storage = QdrantStorage(Settings())
    cluster_vec = [1.0] * 768
    context_cid = "src/a.py:1"
    context_pid = storage.chunk_id_to_point_id(context_cid)
    captured: dict = {}

    async def fake_sample(*args, **kwargs):
        return [(context_cid, context_pid, cluster_vec)]

    async def fake_query_points(**kwargs):
        captured.update(kwargs)
        result = MagicMock()
        result.points = []
        return result

    mock_client = MagicMock()
    mock_client.query_points = AsyncMock(side_effect=fake_query_points)
    monkeypatch.setattr(
        storage, "sample_context_dense_vectors", AsyncMock(side_effect=fake_sample)
    )
    monkeypatch.setattr(storage, "_get_client", AsyncMock(return_value=mock_client))

    await storage.find_outlier_chunks(
        collection="coll",
        context_chunk_ids=[context_cid],
        limit=3,
        path_glob="src/*.py",
    )

    assert captured["limit"] == 9  # limit * 3


@pytest.mark.asyncio
async def test_sample_context_dense_vectors_from_chunk_ids():
    storage = QdrantStorage(Settings())
    chunk_id = "src/foo.py:1"
    point_id = storage.chunk_id_to_point_id(chunk_id)
    dense = [0.1] * 768

    record = MagicMock()
    record.id = point_id
    record.vector = {"dense": dense}

    mock_client = MagicMock()
    mock_client.retrieve = AsyncMock(return_value=[record])
    storage._get_client = AsyncMock(return_value=mock_client)

    samples = await storage.sample_context_dense_vectors(
        "coll",
        context_chunk_ids=[chunk_id],
        max_samples=1,
    )

    assert len(samples) == 1
    assert samples[0][0] == chunk_id
    assert samples[0][1] == point_id
    assert samples[0][2] == dense


@pytest.mark.asyncio
async def test_sample_context_dense_vectors_scroll_respects_path_glob():
    storage = QdrantStorage(Settings())

    def make_point(cid: str, rel_path: str, vec: list[float]):
        point = MagicMock()
        point.id = str(uuid.uuid5(uuid.NAMESPACE_URL, cid))
        point.vector = {"dense": vec}
        point.payload = {"chunk_id": cid, "rel_path": rel_path}
        return point

    mock_client = MagicMock()
    mock_client.retrieve = AsyncMock(return_value=[])
    mock_client.scroll = AsyncMock(
        return_value=(
            [
                make_point("src/a.py:1", "src/a.py", [0.1] * 768),
                make_point("tests/t.py:1", "tests/t.py", [0.2] * 768),
            ],
            None,
        )
    )
    storage._get_client = AsyncMock(return_value=mock_client)

    samples = await storage.sample_context_dense_vectors(
        "coll",
        path_glob="src/*.py",
        max_samples=10,
    )

    assert len(samples) == 1
    assert samples[0][0] == "src/a.py:1"
