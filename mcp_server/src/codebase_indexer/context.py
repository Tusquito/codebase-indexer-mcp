# src/codebase_indexer/context.py
"""Shared application context passed to every MCP tool."""

from __future__ import annotations

from dataclasses import dataclass

from codebase_indexer.config import Settings
from codebase_indexer.index_jobs import IndexJobTracker
from codebase_indexer.indexer.backends.factory import create_backends
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
        dense_backend, sparse_backend = create_backends(settings)
        return cls(
            settings=settings,
            storage=QdrantStorage(settings),
            embedder=Embedder(
                dense_backend=dense_backend,
                sparse_backend=sparse_backend,
                dense_embed_vector_size=settings.dense_embed_vector_size,
                batch_size=settings.batch_size,
                hybrid=settings.hybrid_search,
                memory_warn_pct=settings.memory_pressure_warn_pct,
                memory_halt_pct=settings.memory_pressure_halt_pct,
                sequential_embed=settings.sequential_embed,
            ),
            job_tracker=IndexJobTracker(),
            url_extractors=UrlExtractors(settings.service_url_keyword_list),
        )
