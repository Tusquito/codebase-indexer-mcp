"""Unit tests for the index_all MCP tool."""

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from codebase_indexer.index_jobs import IndexJobTracker, JobStatus
from codebase_indexer.indexer.pipeline import PipelineResult
from fastmcp import FastMCP


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

_GOOD_RESULT = PipelineResult(total_files=5, indexed_files=5, total_chunks=20)


def _stat(name: str) -> SimpleNamespace:
    """Minimal stand-in for CollectionStats (only .name is consumed by index_all)."""
    return SimpleNamespace(name=name)


async def _setup(collection_names: list[str]):
    """
    Wire up a mock AppContext and register index tools on a fresh FastMCP instance.

    Returns ``(index_all_fn, job_tracker, storage_mock)``.
    """
    from codebase_indexer.tools.index import register_index_tool

    storage = AsyncMock()
    storage.list_collection_stats = AsyncMock(
        return_value=[_stat(n) for n in collection_names]
    )

    settings = MagicMock()
    settings.workspace_path = "/workspace"
    settings.release_models_after_index = False

    job_tracker = IndexJobTracker()

    ctx = SimpleNamespace(
        settings=settings,
        storage=storage,
        job_tracker=job_tracker,
        embedder=MagicMock(),
        url_extractors=MagicMock(),
    )

    mcp = FastMCP("test")
    register_index_tool(mcp, ctx)
    tool = await mcp.get_tool("index_all")
    return tool.fn, job_tracker, storage


# ---------------------------------------------------------------------------
# 1. No collections
# ---------------------------------------------------------------------------

class TestIndexAllNoCollections:
    async def test_returns_error_when_no_collections(self):
        index_all, _, _ = await _setup([])

        result = await index_all()

        assert "error" in result
        assert "hint" in result


# ---------------------------------------------------------------------------
# 2. Single collection, wait=True
# ---------------------------------------------------------------------------

class TestIndexAllSingleCollection:
    async def test_wait_true_indexes_and_returns_done(self):
        with patch(
            "codebase_indexer.tools.index.run_pipeline",
            new=AsyncMock(return_value=_GOOD_RESULT),
        ):
            index_all, _, _ = await _setup(["alpha"])
            result = await index_all(wait=True)

        assert result["message"] == "Indexed 1/1 collections"
        assert len(result["results"]) == 1
        assert result["results"][0]["status"] == "done"
        assert result["results"][0]["collection"] == "alpha"


# ---------------------------------------------------------------------------
# 3. Multiple collections, wait=True
# ---------------------------------------------------------------------------

class TestIndexAllMultipleCollections:
    async def test_wait_true_indexes_all_sequentially(self):
        with patch(
            "codebase_indexer.tools.index.run_pipeline",
            new=AsyncMock(return_value=_GOOD_RESULT),
        ):
            index_all, _, _ = await _setup(["alpha", "beta"])
            result = await index_all(wait=True)

        assert result["message"] == "Indexed 2/2 collections"
        assert len(result["results"]) == 2
        statuses = {r["collection"]: r["status"] for r in result["results"]}
        assert statuses == {"alpha": "done", "beta": "done"}


# ---------------------------------------------------------------------------
# 4. Skip running collection
# ---------------------------------------------------------------------------

class TestIndexAllSkipsRunning:
    async def test_running_collection_is_skipped_others_indexed(self):
        with patch(
            "codebase_indexer.tools.index.run_pipeline",
            new=AsyncMock(return_value=_GOOD_RESULT),
        ):
            index_all, job_tracker, _ = await _setup(["alpha", "beta"])

            # Simulate "alpha" already running before index_all is called.
            running_job = await job_tracker.start_job("alpha", "/alpha")
            running_job.status = JobStatus.RUNNING

            result = await index_all(wait=True)

        statuses = {r["collection"]: r["status"] for r in result["results"]}
        assert statuses["alpha"] == "running"
        assert statuses["beta"] == "done"


# ---------------------------------------------------------------------------
# 5. wait=False
# ---------------------------------------------------------------------------

class TestIndexAllWaitFalse:
    async def test_returns_immediately_with_queued_statuses(self):
        with patch(
            "codebase_indexer.tools.index.run_pipeline",
            new=AsyncMock(return_value=_GOOD_RESULT),
        ):
            index_all, _, _ = await _setup(["alpha", "beta"])
            result = await index_all(wait=False)

            # Jobs were started but not awaited — they should be queued at return time.
            assert len(result["results"]) == 2
            for r in result["results"]:
                assert r["status"] == "queued"

            # Drain background tasks while the patch is still active.
            await asyncio.sleep(0)

    async def test_wait_false_skips_running_collection(self):
        with patch(
            "codebase_indexer.tools.index.run_pipeline",
            new=AsyncMock(return_value=_GOOD_RESULT),
        ):
            index_all, job_tracker, _ = await _setup(["alpha", "beta"])

            running_job = await job_tracker.start_job("alpha", "/alpha")
            running_job.status = JobStatus.RUNNING

            result = await index_all(wait=False)

            # "alpha" skipped; "beta" queued
            assert len(result["results"]) == 2
            statuses = {r["collection"]: r["status"] for r in result["results"]}
            assert statuses["alpha"] == "running"
            assert statuses["beta"] == "queued"

            # Drain background tasks while the patch is still active.
            await asyncio.sleep(0)


# ---------------------------------------------------------------------------
# 6. force=True
# ---------------------------------------------------------------------------

class TestIndexAllForceFlag:
    async def test_force_is_forwarded_to_pipeline(self):
        mock_pipeline = AsyncMock(return_value=_GOOD_RESULT)
        with patch("codebase_indexer.tools.index.run_pipeline", new=mock_pipeline):
            index_all, _, _ = await _setup(["alpha"])
            await index_all(force=True, wait=True)

        mock_pipeline.assert_called_once()
        assert mock_pipeline.call_args.kwargs["force"] is True

    async def test_force_false_by_default(self):
        mock_pipeline = AsyncMock(return_value=_GOOD_RESULT)
        with patch("codebase_indexer.tools.index.run_pipeline", new=mock_pipeline):
            index_all, _, _ = await _setup(["alpha"])
            await index_all(wait=True)

        assert mock_pipeline.call_args.kwargs["force"] is False


# ---------------------------------------------------------------------------
# 7. Collection failure — others continue
# ---------------------------------------------------------------------------

class TestIndexAllCollectionFailure:
    async def test_failed_collection_reported_others_continue(self):
        async def flaky_pipeline(
            settings, storage, collection, sub_path, force, cancel_event
        ):
            if collection == "beta":
                raise RuntimeError("disk full")
            return _GOOD_RESULT

        with patch("codebase_indexer.tools.index.run_pipeline", new=flaky_pipeline):
            index_all, _, _ = await _setup(["alpha", "beta"])
            result = await index_all(wait=True)

        statuses = {r["collection"]: r["status"] for r in result["results"]}
        assert statuses["alpha"] == "done"
        assert statuses["beta"] == "failed"
        assert result["message"] == "Indexed 1/2 collections"

    async def test_all_fail_reports_zero_succeeded(self):
        async def always_fail(
            settings, storage, collection, sub_path, force, cancel_event
        ):
            raise RuntimeError("storage unavailable")

        with patch("codebase_indexer.tools.index.run_pipeline", new=always_fail):
            index_all, _, _ = await _setup(["alpha", "beta"])
            result = await index_all(wait=True)

        assert result["message"] == "Indexed 0/2 collections"
        for r in result["results"]:
            assert r["status"] == "failed"
