"""TEI dense + sparse BM25 hybrid embedding facade."""

from __future__ import annotations

import asyncio
import logging
import time
from dataclasses import dataclass

import structlog

from codebase_indexer.indexer.backends.base import (
    DenseEmbedBackend,
    EmbeddingError,
    LateInteractionEmbedBackend,
    SparseEmbedBackend,
    SparseVector,
    trim_memory,
)
from codebase_indexer.indexer.backends.colbert_onnx import ColbertOnnxBackend
from codebase_indexer.indexer.backends.onnx_sparse import OnnxSparseBackend
from codebase_indexer.indexer.chunker import Chunk
from codebase_indexer.memory import check_memory_pressure

log = structlog.get_logger()
_tlog = logging.getLogger(__name__)

__all__ = [
    "Embedder",
    "EmbeddedChunk",
    "SparseVector",
    "EmbeddingError",
    "trim_memory",
]


@dataclass
class EmbeddedChunk:
    chunk: Chunk
    dense_vector: object
    sparse_vector: SparseVector | None
    colbert_vector: list[list[float]] | None = None


class _EmbedderMeta(type):
    @property
    def _shared_sparse_model(cls):
        return OnnxSparseBackend._shared_model


class Embedder(metaclass=_EmbedderMeta):
    """Facade orchestrating TEI dense and sparse BM25 backends."""

    _last_embed_time: float = 0.0
    _idle_timer: asyncio.Task | None = None
    _idle_timeout_s: int = 0
    _registered_backends: list[
        tuple[DenseEmbedBackend, SparseEmbedBackend, LateInteractionEmbedBackend | None]
    ] = []

    def __init__(
        self,
        *,
        dense_backend: DenseEmbedBackend,
        sparse_backend: SparseEmbedBackend,
        dense_embed_vector_size: int,
        batch_size: int = 16,
        hybrid: bool = True,
        memory_warn_pct: int = 70,
        memory_halt_pct: int = 85,
        sequential_embed: bool = False,
        colbert_backend: LateInteractionEmbedBackend | None = None,
        rerank: bool = False,
    ) -> None:
        self.dense_backend = dense_backend
        self.sparse_backend = sparse_backend
        self.colbert_backend = colbert_backend
        self.rerank = rerank and colbert_backend is not None
        self.dense_embed_vector_size = dense_embed_vector_size
        self.batch_size = batch_size
        self.hybrid = hybrid
        self.memory_warn_pct = memory_warn_pct
        self.memory_halt_pct = memory_halt_pct
        self.sequential_embed = sequential_embed
        triple = (dense_backend, sparse_backend, colbert_backend)
        if triple not in Embedder._registered_backends:
            Embedder._registered_backends.append(triple)

    @classmethod
    def release_models(cls) -> None:
        released_dense = False
        released_sparse = False
        released_colbert = False
        for dense, sparse, colbert in cls._registered_backends:
            if dense.is_loaded():
                dense.release()
                released_dense = True
            if sparse.is_loaded():
                sparse.release()
                released_sparse = True
            if colbert is not None and colbert.is_loaded():
                colbert.release()
                released_colbert = True
        OnnxSparseBackend.release_shared()
        if ColbertOnnxBackend._shared_model is not None:
            ColbertOnnxBackend.release_shared()
        trim_memory()
        _tlog.info(
            "models_released dense=%s sparse=%s colbert=%s memory_freed=true",
            released_dense,
            released_sparse,
            released_colbert,
        )

    @classmethod
    def any_models_loaded(cls) -> bool:
        for dense, sparse, colbert in cls._registered_backends:
            if dense.is_loaded() or sparse.is_loaded():
                return True
            if colbert is not None and colbert.is_loaded():
                return True
        return OnnxSparseBackend._shared_model is not None

    @classmethod
    def start_idle_timer(cls, timeout_s: int) -> None:
        cls._idle_timeout_s = timeout_s
        if timeout_s <= 0:
            return
        cls.stop_idle_timer()

        async def _idle_watcher() -> None:
            check_interval = min(60, timeout_s)
            while True:
                await asyncio.sleep(check_interval)
                if not cls.any_models_loaded():
                    continue
                idle_s = time.monotonic() - cls._last_embed_time
                if idle_s >= timeout_s:
                    _tlog.info(
                        "idle_timeout_releasing_models idle_s=%.0f timeout_s=%d",
                        idle_s,
                        timeout_s,
                    )
                    cls.release_models()

        try:
            loop = asyncio.get_running_loop()
            cls._idle_timer = loop.create_task(
                _idle_watcher(), name="embedder-idle-timer"
            )
        except RuntimeError:
            pass

    @classmethod
    def _ensure_idle_timer(cls) -> None:
        if cls._idle_timeout_s <= 0:
            return
        if cls._idle_timer is not None and not cls._idle_timer.done():
            return
        cls.start_idle_timer(cls._idle_timeout_s)

    @classmethod
    def stop_idle_timer(cls) -> None:
        if cls._idle_timer is not None and not cls._idle_timer.done():
            cls._idle_timer.cancel()
        cls._idle_timer = None

    def preload(self) -> None:
        self.dense_backend.preload()
        if self.hybrid:
            self.sparse_backend.preload()
        if self.rerank and self.colbert_backend is not None:
            self.colbert_backend.preload()

    def _get_sparse_model(self):
        if isinstance(self.sparse_backend, OnnxSparseBackend):
            return self.sparse_backend._get_sparse_model()
        raise AttributeError("_get_sparse_model only available for ONNX sparse backend")

    def _embed_sparse_batch_sync(self, texts: list[str]) -> list[SparseVector]:
        if isinstance(self.sparse_backend, OnnxSparseBackend):
            return self.sparse_backend._embed_sparse_batch_sync(texts)
        raise AttributeError("_embed_sparse_batch_sync only for ONNX sparse backend")

    async def embed_batch_dense(self, texts: list[str]) -> list[list[float]]:
        return await self.dense_backend.embed_batch(texts)

    async def embed_query(
        self, text: str, *, rerank: bool | None = None
    ) -> tuple[list[float], SparseVector | None, list[list[float]] | None]:
        Embedder._last_embed_time = time.monotonic()
        Embedder._ensure_idle_timer()
        if self.hybrid:
            dense_list, sparse_list = await asyncio.gather(
                self.dense_backend.embed_query([text]),
                self.sparse_backend.embed_batch([text]),
            )
            dense_vec, sparse_vec = dense_list[0], sparse_list[0]
        else:
            dense_list = await self.dense_backend.embed_query([text])
            dense_vec, sparse_vec = dense_list[0], None

        use_rerank = self.rerank and rerank is not False
        colbert_vec: list[list[float]] | None = None
        if use_rerank and self.colbert_backend is not None:
            colbert_list = await self.colbert_backend.embed_batch([text])
            colbert_vec = colbert_list[0]
        return dense_vec, sparse_vec, colbert_vec

    async def embed_queries(
        self, texts: list[str], *, rerank: bool | None = None
    ) -> list[tuple[list[float], SparseVector | None, list[list[float]] | None]]:
        Embedder._last_embed_time = time.monotonic()
        Embedder._ensure_idle_timer()
        if self.hybrid:
            dense_list, sparse_list = await asyncio.gather(
                self.dense_backend.embed_query(texts),
                self.sparse_backend.embed_batch(texts),
            )
        else:
            dense_list = await self.dense_backend.embed_query(texts)
            sparse_list = [None] * len(texts)  # type: ignore[list-item]

        use_rerank = self.rerank and rerank is not False
        colbert_list: list[list[list[float]] | None]
        if use_rerank and self.colbert_backend is not None:
            colbert_raw = await self.colbert_backend.embed_batch(texts)
            colbert_list = colbert_raw
        else:
            colbert_list = [None] * len(texts)

        return list(zip(dense_list, sparse_list, colbert_list))

    async def embed_chunks(self, chunks: list[Chunk]) -> list[EmbeddedChunk]:
        Embedder._last_embed_time = time.monotonic()
        Embedder._ensure_idle_timer()
        texts = [c.content for c in chunks]
        t_start = time.monotonic()

        severity, pct = check_memory_pressure(
            self.memory_warn_pct, self.memory_halt_pct
        )
        force_sequential = self.sequential_embed or severity in ("warn", "halt")

        if self.hybrid and not force_sequential:
            dense_vectors, sparse_vectors = await asyncio.gather(
                self.dense_backend.embed_batch(texts),
                self.sparse_backend.embed_batch(texts),
            )
        elif self.hybrid:
            reason = "sequential_embed" if self.sequential_embed else "memory_pressure"
            log.info(
                "embed_sequential_mode",
                reason=reason,
                pressure_pct=pct if reason == "memory_pressure" else None,
            )
            dense_vectors = await self.dense_backend.embed_batch(texts)
            sparse_vectors = await self.sparse_backend.embed_batch(texts)
        else:
            dense_vectors = await self.dense_backend.embed_batch(texts)
            sparse_vectors = [None] * len(chunks)  # type: ignore[list-item]

        colbert_vectors: list[list[list[float]] | None] = [None] * len(chunks)
        if self.rerank and self.colbert_backend is not None:
            log.info("embed_colbert_sequential", chunks=len(chunks))
            colbert_raw = await self.colbert_backend.embed_batch(texts)
            colbert_vectors = colbert_raw

        log.info(
            "embed_chunks_complete",
            chunks=len(chunks),
            hybrid=self.hybrid,
            rerank=self.rerank,
            dense_backend="tei",
            total_elapsed_s=round(time.monotonic() - t_start, 2),
        )

        return [
            EmbeddedChunk(
                chunk=chunk,
                dense_vector=dv,
                sparse_vector=sv,
                colbert_vector=cv,
            )
            for chunk, dv, sv, cv in zip(chunks, dense_vectors, sparse_vectors, colbert_vectors)
        ]
