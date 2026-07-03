"""Unit tests for shared search dispatch helpers."""

from unittest.mock import AsyncMock, MagicMock

import pytest

from codebase_indexer.indexer.embedder import SparseVector
from codebase_indexer.tools.search_common import dispatch_search, run_search

_DENSE = [0.1, 0.2]
_SPARSE = SparseVector(indices=[1], values=[1.0])
_COLBERT = [[0.3, 0.4], [0.5, 0.6]]


@pytest.mark.asyncio
async def test_dispatch_search_single_collection_passes_colbert():
    storage = AsyncMock()
    storage.search = AsyncMock(return_value=[])

    await dispatch_search(
        storage,
        _DENSE,
        _SPARSE,
        _COLBERT,
        ["proj-a"],
        top_k=5,
        language=None,
        min_score=0.3,
    )

    storage.search.assert_awaited_once_with(
        collection="proj-a",
        dense_vector=_DENSE,
        sparse_vector=_SPARSE,
        colbert_vector=_COLBERT,
        top_k=5,
        language=None,
        min_score=0.3,
    )


@pytest.mark.asyncio
async def test_dispatch_search_multi_collection_passes_colbert():
    storage = AsyncMock()
    storage.search = AsyncMock(return_value=[])

    await dispatch_search(
        storage,
        _DENSE,
        _SPARSE,
        _COLBERT,
        ["proj-a", "proj-b"],
        top_k=10,
        language="python",
        min_score=0.25,
    )

    storage.search.assert_awaited_once_with(
        collection=None,
        dense_vector=_DENSE,
        sparse_vector=_SPARSE,
        colbert_vector=_COLBERT,
        top_k=10,
        language="python",
        min_score=0.25,
        restrict_collections=["proj-a", "proj-b"],
    )


@pytest.mark.asyncio
async def test_run_search_forwards_colbert_from_embedder():
    storage = AsyncMock()
    storage.search = AsyncMock(return_value=[])
    embedder = MagicMock()
    embedder.embed_query = AsyncMock(return_value=(_DENSE, _SPARSE, _COLBERT))

    await run_search(
        storage,
        embedder,
        "find auth handler",
        ["proj-a", "proj-b"],
        top_k=7,
        language=None,
        min_score=0.3,
    )

    embedder.embed_query.assert_awaited_once_with("find auth handler", rerank=None)
    storage.search.assert_awaited_once_with(
        collection=None,
        dense_vector=_DENSE,
        sparse_vector=_SPARSE,
        colbert_vector=_COLBERT,
        top_k=7,
        language=None,
        min_score=0.3,
        restrict_collections=["proj-a", "proj-b"],
    )


@pytest.mark.asyncio
async def test_run_search_rerank_false_passes_to_embedder():
    storage = AsyncMock()
    storage.search = AsyncMock(return_value=[])
    embedder = MagicMock()
    embedder.embed_query = AsyncMock(return_value=(_DENSE, _SPARSE, None))

    await run_search(
        storage,
        embedder,
        "fast probe",
        ["proj-a"],
        top_k=5,
        language=None,
        min_score=0.3,
        rerank=False,
    )

    embedder.embed_query.assert_awaited_once_with("fast probe", rerank=False)
    storage.search.assert_awaited_once_with(
        collection="proj-a",
        dense_vector=_DENSE,
        sparse_vector=_SPARSE,
        colbert_vector=None,
        top_k=5,
        language=None,
        min_score=0.3,
    )


@pytest.mark.asyncio
async def test_run_search_rerank_none_default():
    storage = AsyncMock()
    storage.search = AsyncMock(return_value=[])
    embedder = MagicMock()
    embedder.embed_query = AsyncMock(return_value=(_DENSE, _SPARSE, _COLBERT))

    await run_search(
        storage,
        embedder,
        "default rerank",
        ["proj-a"],
        top_k=5,
        language=None,
        min_score=0.3,
        rerank=None,
    )

    embedder.embed_query.assert_awaited_once_with("default rerank", rerank=None)
    assert storage.search.await_args.kwargs["colbert_vector"] == _COLBERT
