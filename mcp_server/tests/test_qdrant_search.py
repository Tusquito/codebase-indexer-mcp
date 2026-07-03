"""Unit tests for Qdrant search helpers (no live Qdrant required)."""

import pytest

from codebase_indexer.config import Settings
from codebase_indexer.indexer.embedder import SparseVector
from codebase_indexer.storage.qdrant import QdrantStorage, SearchResult, fuse_cross_collection_rrf


def _result(chunk_id: str, collection: str, score: float) -> SearchResult:
    return SearchResult(
        chunk_id=chunk_id,
        score=score,
        rel_path=f"{chunk_id}.py",
        language="python",
        start_line=1,
        end_line=2,
        symbol_name=None,
        symbol_type="other",
        content="",
        collection=collection,
    )


def test_dense_search_params_with_quantization():
    storage = QdrantStorage(Settings(quantization=True, quant_oversampling=3.0, hnsw_ef=128))
    params = storage._dense_search_params()

    assert params.hnsw_ef == 128
    assert params.quantization is not None
    assert params.quantization.rescore is True
    assert params.quantization.oversampling == 3.0


def test_dense_search_params_without_quantization_still_sets_hnsw_ef():
    storage = QdrantStorage(Settings(quantization=False, hnsw_ef=96))
    params = storage._dense_search_params()

    assert params.hnsw_ef == 96
    assert params.quantization is None


def test_fuse_cross_collection_rrf_ranks_by_global_rrf_not_raw_score():
    """Rank-1 in a weaker collection beats rank-2 with inflated RRF score."""
    coll_a = [
        _result("a1", "repo-a", score=0.9),
        _result("a2", "repo-a", score=0.1),
    ]
    coll_b = [
        _result("b1", "repo-b", score=0.99),
        _result("b2", "repo-b", score=0.5),
    ]

    fused = fuse_cross_collection_rrf([coll_a, coll_b], rrf_k=60, top_k=4)
    ids = [r.chunk_id for r in fused]

    # Rank-1 hits beat rank-2 regardless of inflated per-collection RRF scores.
    assert ids.index("a1") < ids.index("b2")
    assert ids.index("b1") < ids.index("a2")
    # Both rank-1 hits lead; among equal fused scores, repo-b wins the tie-break.
    assert set(ids[:2]) == {"a1", "b1"}
    assert ids[0] == "b1"
    assert fused[0].score == 0.99


def test_fuse_cross_collection_rrf_respects_top_k():
    coll_a = [_result("a1", "repo-a", 1.0)]
    coll_b = [_result("b1", "repo-b", 1.0)]

    fused = fuse_cross_collection_rrf([coll_a, coll_b], rrf_k=60, top_k=1)

    assert len(fused) == 1


def test_fuse_cross_collection_rrf_rank_two_with_high_score_loses_to_rank_one():
    """Inflated per-collection RRF score at rank 2 must not outrank rank 1."""
    coll_a = [_result("winner", "repo-a", score=0.01)]
    coll_b = [
        _result("runner-up", "repo-b", score=0.5),
        _result("inflated", "repo-b", score=0.99),
    ]

    fused = fuse_cross_collection_rrf([coll_a, coll_b], rrf_k=60, top_k=2)

    # Rank-1 across collections ties; rank-2 with inflated score is excluded from top 2.
    assert [r.chunk_id for r in fused] == ["runner-up", "winner"]
    assert "inflated" not in [r.chunk_id for r in fused]


def test_fuse_cross_collection_rrf_tie_breaks_by_collection_then_chunk_id():
    """Equal fused RRF scores resolve deterministically by (collection, chunk_id)."""
    coll_a = [_result("chunk", "repo-z", score=0.1)]
    coll_b = [_result("chunk", "repo-a", score=0.9)]

    fused = fuse_cross_collection_rrf([coll_a, coll_b], rrf_k=60, top_k=2)

    assert [r.collection for r in fused] == ["repo-z", "repo-a"]


def test_fuse_cross_collection_rrf_empty_input():
    assert fuse_cross_collection_rrf([], rrf_k=60, top_k=5) == []


def test_fuse_cross_collection_rrf_mixed_empty_and_populated():
    coll_b = [_result("only", "repo-b", score=0.5)]

    fused = fuse_cross_collection_rrf([[], coll_b], rrf_k=60, top_k=3)

    assert len(fused) == 1
    assert fused[0].chunk_id == "only"


@pytest.mark.asyncio
async def test_search_rerank_uses_rerank_prefetch_limit(monkeypatch):
    """When rerank is enabled, prefetch limit must be rerank_prefetch not top_k * multiplier."""
    from unittest.mock import AsyncMock, MagicMock

    settings = Settings(rerank_enabled=True, rerank_prefetch=77, prefetch_multiplier=5)
    storage = QdrantStorage(settings)

    captured: dict = {}

    async def fake_query_points(**kwargs):
        captured.update(kwargs)
        point = MagicMock()
        point.score = 0.9
        point.payload = {
            "chunk_id": "c1",
            "rel_path": "a.py",
            "language": "python",
            "start_line": 1,
            "end_line": 2,
            "symbol_name": None,
            "symbol_type": "other",
            "content": "x",
        }
        result = MagicMock()
        result.points = [point]
        return result

    mock_client = MagicMock()
    mock_client.query_points = AsyncMock(side_effect=fake_query_points)
    monkeypatch.setattr(storage, "_get_client", AsyncMock(return_value=mock_client))

    dense = [0.1] * 768
    sparse = SparseVector(indices=[1], values=[0.5])
    colbert = [[1.0] * 128, [0.5] * 128]

    await storage._search_single(
        "coll",
        dense,
        sparse,
        top_k=5,
        language=None,
        min_score=0.5,
        colbert_vector=colbert,
    )

    assert captured["using"] == "colbert"
    assert captured["limit"] == 5
    prefetch = captured["prefetch"]
    assert len(prefetch) == 2
    assert prefetch[0].limit == 77
    assert prefetch[1].limit == 77


def _mock_point(score: float, chunk_id: str = "c1"):
    from unittest.mock import MagicMock

    point = MagicMock()
    point.score = score
    point.payload = {
        "chunk_id": chunk_id,
        "rel_path": "a.py",
        "language": "python",
        "start_line": 1,
        "end_line": 2,
        "symbol_name": None,
        "symbol_type": "other",
        "content": "x",
    }
    return point


@pytest.mark.asyncio
async def test_adaptive_rerank_skips_colbert_on_large_gap(monkeypatch):
    """Large RRF gap → hybrid probe only, no ColBERT query."""
    from unittest.mock import AsyncMock, MagicMock

    settings = Settings(
        rerank_enabled=True,
        rerank_adaptive_enabled=True,
        rerank_adaptive_gap=0.02,
    )
    storage = QdrantStorage(settings)
    calls: list[dict] = []

    async def fake_query_points(**kwargs):
        calls.append(kwargs)
        result = MagicMock()
        if kwargs.get("using") == "colbert":
            result.points = [_mock_point(0.9)]
        else:
            result.points = [_mock_point(0.10), _mock_point(0.05)]
        return result

    mock_client = MagicMock()
    mock_client.query_points = AsyncMock(side_effect=fake_query_points)
    monkeypatch.setattr(storage, "_get_client", AsyncMock(return_value=mock_client))

    dense = [0.1] * 768
    sparse = SparseVector(indices=[1], values=[0.5])
    colbert = [[1.0] * 128]

    await storage._search_single(
        "coll", dense, sparse, top_k=5, language=None, min_score=0.5, colbert_vector=colbert
    )

    assert len(calls) == 1
    assert calls[0].get("using") != "colbert"
    assert "using" not in calls[0]
    stats = storage.adaptive_rerank_stats
    assert stats.total == 1
    assert stats.skipped == 1
    assert stats.reranked == 0


@pytest.mark.asyncio
async def test_adaptive_rerank_runs_colbert_on_small_gap(monkeypatch):
    """Small RRF gap → hybrid probe then ColBERT rerank."""
    from unittest.mock import AsyncMock, MagicMock

    settings = Settings(
        rerank_enabled=True,
        rerank_adaptive_enabled=True,
        rerank_adaptive_gap=0.02,
    )
    storage = QdrantStorage(settings)
    calls: list[dict] = []

    async def fake_query_points(**kwargs):
        calls.append(kwargs)
        result = MagicMock()
        if kwargs.get("using") == "colbert":
            result.points = [_mock_point(0.9)]
        else:
            result.points = [_mock_point(0.10), _mock_point(0.09)]
        return result

    mock_client = MagicMock()
    mock_client.query_points = AsyncMock(side_effect=fake_query_points)
    monkeypatch.setattr(storage, "_get_client", AsyncMock(return_value=mock_client))

    dense = [0.1] * 768
    sparse = SparseVector(indices=[1], values=[0.5])
    colbert = [[1.0] * 128]

    await storage._search_single(
        "coll", dense, sparse, top_k=5, language=None, min_score=0.5, colbert_vector=colbert
    )

    assert len(calls) == 2
    assert calls[0].get("using") != "colbert"
    assert calls[1]["using"] == "colbert"
    stats = storage.adaptive_rerank_stats
    assert stats.total == 1
    assert stats.skipped == 0
    assert stats.reranked == 1


@pytest.mark.asyncio
async def test_adaptive_rerank_disabled_uses_colbert_only(monkeypatch):
    """Adaptive off → single ColBERT call (existing rerank path)."""
    from unittest.mock import AsyncMock, MagicMock

    settings = Settings(
        rerank_enabled=True,
        rerank_adaptive_enabled=False,
        rerank_prefetch=77,
        prefetch_multiplier=5,
    )
    storage = QdrantStorage(settings)
    calls: list[dict] = []

    async def fake_query_points(**kwargs):
        calls.append(kwargs)
        result = MagicMock()
        result.points = [_mock_point(0.9)]
        return result

    mock_client = MagicMock()
    mock_client.query_points = AsyncMock(side_effect=fake_query_points)
    monkeypatch.setattr(storage, "_get_client", AsyncMock(return_value=mock_client))

    dense = [0.1] * 768
    sparse = SparseVector(indices=[1], values=[0.5])
    colbert = [[1.0] * 128, [0.5] * 128]

    await storage._search_single(
        "coll", dense, sparse, top_k=5, language=None, min_score=0.5, colbert_vector=colbert
    )

    assert len(calls) == 1
    assert calls[0]["using"] == "colbert"
    assert storage.adaptive_rerank_stats.total == 0


@pytest.mark.asyncio
async def test_adaptive_rerank_stats_reset(monkeypatch):
    from unittest.mock import AsyncMock, MagicMock

    settings = Settings(rerank_enabled=True, rerank_adaptive_enabled=True)
    storage = QdrantStorage(settings)

    async def fake_query_points(**kwargs):
        result = MagicMock()
        result.points = [_mock_point(0.10), _mock_point(0.05)]
        return result

    mock_client = MagicMock()
    mock_client.query_points = AsyncMock(side_effect=fake_query_points)
    monkeypatch.setattr(storage, "_get_client", AsyncMock(return_value=mock_client))

    dense = [0.1] * 768
    sparse = SparseVector(indices=[1], values=[0.5])
    colbert = [[1.0] * 128]

    await storage._search_single(
        "coll", dense, sparse, top_k=5, language=None, min_score=0.5, colbert_vector=colbert
    )
    assert storage.adaptive_rerank_stats.total == 1

    storage.reset_adaptive_stats()
    assert storage.adaptive_rerank_stats.total == 0
    assert storage.adaptive_rerank_stats.skipped == 0
    assert storage.adaptive_rerank_stats.reranked == 0

