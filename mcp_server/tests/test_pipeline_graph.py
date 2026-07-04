"""Pipeline graph hook tests (mock Neo4j storage)."""

from __future__ import annotations

from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from codebase_indexer.config import Settings
from codebase_indexer.indexer.chunker import Chunk
from codebase_indexer.indexer.pipeline import _flush_double_buffered, run_pipeline
from codebase_indexer.indexer.pipeline import PipelineResult
from codebase_indexer.tools.cross_references import UrlExtractors


def _settings(**overrides) -> Settings:
    base = dict(
        dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=768,
        sparse_threads=2,
        graph_enabled=True,
        neo4j_password="secret",
        preload_models=False,
        flush_every=10000,
    )
    base.update(overrides)
    return Settings(**base)


@pytest.mark.asyncio
async def test_flush_double_buffered_writes_graph_after_upsert():
    settings = _settings()
    graph_storage = MagicMock()
    graph_storage.enabled = True
    graph_storage.write_batch = AsyncMock()

    embedder = MagicMock()
    embedder.memory_warn_pct = 70
    embedder.memory_halt_pct = 85
    embedder.embed_chunks = AsyncMock(return_value=[])

    storage = MagicMock()
    storage.upsert_chunks = AsyncMock()

    chunks = [
        Chunk(
            chunk_id="id1",
            content="def hello(): pass",
            rel_path="a.py",
            language="python",
            start_line=1,
            end_line=1,
            symbol_name="hello",
            symbol_type="function",
            file_sha256="x",
        )
    ]
    result = PipelineResult()

    with patch(
        "codebase_indexer.indexer.pipeline.write_chunks_to_graph",
        new_callable=AsyncMock,
    ) as mock_write:
        task = await _flush_double_buffered(
            chunks,
            embedder,
            storage,
            "demo",
            result,
            None,
            settings=settings,
            graph_storage=graph_storage,
            url_extractors=UrlExtractors(),
            collection_names=["demo"],
        )
        assert task is not None
        await task

        storage.upsert_chunks.assert_awaited_once()
        upsert_kwargs = storage.upsert_chunks.await_args.kwargs
        assert upsert_kwargs.get("omit_callees") is True
        mock_write.assert_awaited_once()
        assert mock_write.await_args.kwargs["collection"] == "demo"


@pytest.mark.asyncio
async def test_run_pipeline_graph_disabled_skips_schema():
    settings = _settings(graph_enabled=False)
    storage = MagicMock()
    storage.ensure_collection = AsyncMock()
    storage.get_file_metadata = AsyncMock(return_value={})
    storage.set_indexing = AsyncMock()
    storage.list_collection_stats = AsyncMock(return_value=[])

    graph_storage = MagicMock()
    graph_storage.enabled = False

    with patch("codebase_indexer.indexer.pipeline.scan_files") as mock_scan:
        async def _empty_scan(*args, **kwargs):
            if False:
                yield  # pragma: no cover
            return

        mock_scan.return_value = _empty_scan()

        result = await run_pipeline(
            settings=settings,
            storage=storage,
            collection="demo",
            graph_storage=graph_storage,
        )

    graph_storage.ensure_schema.assert_not_called()
    assert result.errors == []


@pytest.mark.asyncio
async def test_run_pipeline_graph_error_appended_to_result():
    settings = _settings()
    storage = MagicMock()
    storage.ensure_collection = AsyncMock()
    storage.get_file_metadata = AsyncMock(return_value={})
    storage.set_indexing = AsyncMock()
    storage.list_collection_stats = AsyncMock(return_value=[])

    graph_storage = MagicMock()
    graph_storage.enabled = True
    graph_storage.ensure_schema = AsyncMock(side_effect=RuntimeError("neo4j down"))

    with patch("codebase_indexer.indexer.pipeline.scan_files") as mock_scan:
        async def _empty_scan(*args, **kwargs):
            if False:
                yield  # pragma: no cover
            return

        mock_scan.return_value = _empty_scan()

        result = await run_pipeline(
            settings=settings,
            storage=storage,
            collection="demo",
            graph_storage=graph_storage,
        )

    assert any("Graph schema init error" in err for err in result.errors)


@pytest.mark.asyncio
async def test_run_pipeline_stamps_graph_call_sites_metadata():
    settings = _settings()
    storage = MagicMock()
    storage.ensure_collection = AsyncMock()
    storage.get_file_metadata = AsyncMock(return_value={})
    storage.set_indexing = AsyncMock()
    storage.list_collection_stats = AsyncMock(return_value=[])
    storage.set_collection_graph_call_sites = AsyncMock()

    graph_storage = MagicMock()
    graph_storage.enabled = True
    graph_storage.ensure_schema = AsyncMock()

    with patch("codebase_indexer.indexer.pipeline.scan_files") as mock_scan:
        async def _empty_scan(*args, **kwargs):
            if False:
                yield  # pragma: no cover
            return

        mock_scan.return_value = _empty_scan()

        await run_pipeline(
            settings=settings,
            storage=storage,
            collection="demo",
            graph_storage=graph_storage,
        )

    storage.set_collection_graph_call_sites.assert_awaited_once_with("demo", True)
