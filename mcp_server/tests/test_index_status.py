"""Tests for live indexing progress exposed via index_status."""

import asyncio
from unittest.mock import AsyncMock, MagicMock, patch

from codebase_indexer.index_jobs import IndexJob, IndexJobTracker
from codebase_indexer.indexer.pipeline import PipelineResult, run_pipeline
from codebase_indexer.indexer.scanner import FileRecord
from codebase_indexer.tools.index import _run_index_job


class TestIndexJobLiveCounters:
    def test_to_dict_reads_from_shared_result(self):
        job = IndexJob(collection="alpha", path="/alpha")
        shared = PipelineResult(
            total_files=10,
            indexed_files=3,
            skipped_files=7,
            total_chunks=42,
            errors=["warn"],
        )
        job._result = shared

        d = job.to_dict()

        assert d["total_files"] == 10
        assert d["indexed_files"] == 3
        assert d["skipped_files"] == 7
        assert d["total_chunks"] == 42
        assert d["errors"] == ["warn"]

    def test_to_dict_returns_zeros_without_result(self):
        job = IndexJob(collection="alpha", path="/alpha")

        d = job.to_dict()

        assert d["total_files"] == 0
        assert d["indexed_files"] == 0
        assert d["skipped_files"] == 0
        assert d["total_chunks"] == 0
        assert d["errors"] == []


class TestRunPipelineExternalResult:
    async def test_run_pipeline_mutates_and_returns_passed_result(self):
        shared = PipelineResult()

        async def fake_scan(*_args, **_kwargs):
            yield FileRecord(
                abs_path="/workspace/proj/a.py",
                rel_path="proj/a.py",
                language="python",
                content="",
                sha256_hash="abc",
                mtime_skipped=True,
            )

        storage = AsyncMock()
        storage.ensure_collection = AsyncMock()
        storage.get_file_metadata = AsyncMock(return_value={})
        storage.set_indexing = AsyncMock()

        settings = MagicMock()
        settings.qdrant_collection = "test-coll"
        settings.workspace_path = "/workspace"
        settings.readahead_buffer = 4
        settings.excluded_dirs_set = set()
        settings.flush_every = 10_000
        settings.dense_embed_model = "dense"
        settings.sparse_embed_model = "sparse"
        settings.dense_embed_vector_size = 384
        settings.batch_size = 8
        settings.hybrid_search = True
        settings.sparse_threads = 1
        settings.max_dense_embed_tokens = 512
        settings.max_sparse_embed_tokens = 512
        settings.memory_pressure_warn_pct = 80
        settings.memory_pressure_halt_pct = 95
        settings.sequential_embed = False
        settings.max_chunk_lines = 150
        settings.chunk_overlap_lines = 10
        settings.release_models_after_index = False

        with (
            patch("codebase_indexer.indexer.pipeline.scan_files", fake_scan),
            patch("codebase_indexer.indexer.pipeline.Embedder"),
        ):
            returned = await run_pipeline(
                settings=settings,
                storage=storage,
                collection="test-coll",
                sub_path="/proj",
                result=shared,
            )

        assert returned is shared
        assert shared.total_files == 1
        assert shared.skipped_files == 1
        assert shared.indexed_files == 0
        assert shared.total_chunks == 0


class TestRunIndexJobLiveProgress:
    async def test_index_status_sees_counters_while_pipeline_runs(self):
        started = asyncio.Event()
        release = asyncio.Event()

        async def slow_pipeline(
            settings,
            storage,
            collection,
            sub_path,
            force,
            cancel_event=None,
            result=None,
            graph_storage=None,
            url_extractors=None,
        ):
            assert result is not None
            result.total_files = 5
            result.indexed_files = 2
            result.skipped_files = 3
            result.total_chunks = 10
            started.set()
            await release.wait()
            return result

        job_tracker = IndexJobTracker()
        job = await job_tracker.start_job("alpha", "/alpha")

        settings = MagicMock()
        settings.release_models_after_index = False
        storage = AsyncMock()

        with patch(
            "codebase_indexer.tools.index.run_pipeline",
            new=AsyncMock(side_effect=slow_pipeline),
        ):
            task = asyncio.create_task(
                _run_index_job(
                    job_tracker, "alpha", settings, storage, "/alpha", force=False
                )
            )

            await started.wait()
            live = job.to_dict()
            assert live["status"] == "running"
            assert live["total_files"] == 5
            assert live["indexed_files"] == 2
            assert live["skipped_files"] == 3
            assert live["total_chunks"] == 10

            release.set()
            await task

        done = job.to_dict()
        assert done["status"] == "done"
        assert done["total_files"] == 5
        assert done["total_chunks"] == 10

    async def test_cancelled_job_keeps_partial_counters(self):
        from codebase_indexer.indexer.pipeline import IndexCancelled

        async def cancel_mid_pipeline(
            settings,
            storage,
            collection,
            sub_path,
            force,
            cancel_event=None,
            result=None,
            graph_storage=None,
            url_extractors=None,
        ):
            assert result is not None
            result.total_files = 3
            result.total_chunks = 1
            raise IndexCancelled("stopped early")

        job_tracker = IndexJobTracker()
        job = await job_tracker.start_job("beta", "/beta")
        settings = MagicMock()
        settings.release_models_after_index = False

        with patch(
            "codebase_indexer.tools.index.run_pipeline",
            new=AsyncMock(side_effect=cancel_mid_pipeline),
        ):
            await _run_index_job(
                job_tracker, "beta", settings, AsyncMock(), "/beta", force=False
            )

        d = job.to_dict()
        assert d["status"] == "cancelled"
        assert d["total_files"] == 3
        assert d["total_chunks"] == 1
