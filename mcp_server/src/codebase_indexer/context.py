# src/codebase_indexer/context.py
"""Shared application context passed to every MCP tool.

Bundling the long-lived dependencies in one object means each tool takes a
single ``ctx`` argument instead of threading (settings, storage, embedder,
job_tracker, ...) individually, and the wiring lives in ``create_app()``.
"""

from __future__ import annotations

from dataclasses import dataclass

from codebase_indexer.config import Settings
from codebase_indexer.index_jobs import IndexJobTracker
from codebase_indexer.indexer.embedder import Embedder
from codebase_indexer.storage.qdrant import QdrantStorage
from codebase_indexer.tools.cross_references import UrlExtractors


@dataclass
class AppContext:
    settings: Settings
    storage: QdrantStorage
    embedder: Embedder
    job_tracker: IndexJobTracker
    url_extractors: UrlExtractors

    @classmethod
    def create(cls, settings: Settings) -> "AppContext":
        """Build the context (cheap objects only — no model preload here)."""
        return cls(
            settings=settings,
            storage=QdrantStorage(settings),
            embedder=Embedder(
                dense_model=settings.dense_embed_model,
                sparse_model=settings.sparse_embed_model,
                dense_embed_vector_size=settings.dense_embed_vector_size,
                batch_size=settings.batch_size,
                hybrid=settings.hybrid_search,
                dense_threads=settings.dense_threads,
                sparse_threads=settings.sparse_threads,
                max_dense_embed_chars=settings.max_dense_embed_chars,
                max_sparse_embed_chars=settings.max_sparse_embed_chars,
            ),
            job_tracker=IndexJobTracker(),
            url_extractors=UrlExtractors(settings.service_url_keyword_list),
        )
