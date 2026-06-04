# src/codebase_indexer/indexer/embedder.py
"""Dense (fastembed ONNX) + sparse (fastembed) hybrid embedding client."""

import asyncio
import ctypes
import gc
import logging
import os
import statistics
import time
from dataclasses import dataclass
from typing import Any

import structlog

from codebase_indexer.config import KNOWN_EMBED_MODEL_MAX_TOKENS
from codebase_indexer.indexer.chunker import Chunk
from codebase_indexer.indexer.truncation import (
    TruncationSource,
    resolve_max_embed_tokens,
    truncate_for_embedding,
)
from codebase_indexer.memory import (
    check_memory_pressure,
    emergency_trim,
    get_rss_mb,
)

log = structlog.get_logger()
# stdlib logger for sync methods running inside thread-pool workers.
# structlog can silently drop logs from threads; stdlib logging is guaranteed thread-safe.
_tlog = logging.getLogger(__name__)


def trim_memory() -> None:
    """Return freed native allocations to the OS (Linux/glibc only).

    Python's allocator and ONNX Runtime hold freed memory in arenas that
    inflate RSS. Calling malloc_trim after a large batch hands it back so
    long indexing jobs don't accumulate unbounded resident memory.
    """
    gc.collect()
    try:
        ctypes.CDLL("libc.so.6").malloc_trim(0)
    except Exception:
        # Non-glibc platforms (e.g. local dev on Windows/macOS) — no-op.
        pass


def _resolve_threads(explicit: int) -> int:
    """Resolve ONNX thread count: explicit value, else OMP env, else auto.

    Auto leaves ~25% of cores for Qdrant indexing, the sparse worker, and the
    asyncio loop, so the same image scales sensibly across machine sizes.
    """
    if explicit and explicit > 0:
        return explicit
    env = os.environ.get("OMP_NUM_THREADS")
    if env:
        try:
            return max(1, int(env))
        except ValueError:
            pass
    cpu = os.cpu_count() or 8
    return max(1, int(cpu * 0.75))


def _extract_onnx_inner(fastembed_wrapper: Any) -> Any | None:
    """Return the underlying ONNX/BM25 implementation from a fastembed wrapper."""
    return getattr(fastembed_wrapper, "model", None)


def _extract_tokenizer(fastembed_wrapper: Any) -> Any | None:
    inner = _extract_onnx_inner(fastembed_wrapper)
    if inner is None:
        return None
    return getattr(inner, "tokenizer", None)


def _extract_model_dir(fastembed_wrapper: Any) -> Any:
    inner = _extract_onnx_inner(fastembed_wrapper)
    if inner is None:
        return None
    return getattr(inner, "_model_dir", None)


class EmbeddingError(Exception):
    pass


@dataclass
class SparseVector:
    indices: list[int]
    values: list[float]


@dataclass
class EmbeddedChunk:
    chunk: Chunk
    # numpy float32 array (kept un-listed to save RAM); converted to a plain
    # list lazily at Qdrant upsert time.
    dense_vector: object
    sparse_vector: SparseVector | None


class Embedder:
    """Local embedder: fastembed ONNX dense + configurable sparse vectors.

    Model names come from settings (DENSE_EMBED_MODEL, SPARSE_EMBED_MODEL).
    Models are loaded once at startup and shared across all instances.
    No external services required — fully self-contained.
    """

    # Class-level model cache — loaded once, reused by all instances
    _shared_dense_model = None
    _shared_sparse_model = None
    # Tokenizers and resolved limits persist across ONNX release (lightweight).
    _shared_dense_tokenizer: Any | None = None
    _shared_sparse_tokenizer: Any | None = None
    _shared_dense_max_tokens: int = 0
    _shared_sparse_max_tokens: int = 0
    _shared_dense_truncation_source: TruncationSource = "disabled"
    _shared_sparse_truncation_source: TruncationSource = "disabled"

    # Idle-timeout tracking — updated on every embed call; checked by background task
    _last_embed_time: float = 0.0
    _idle_timer: "asyncio.Task | None" = None
    # Configured via start_idle_timer(); 0 = disabled
    _idle_timeout_s: int = 0

    @classmethod
    def release_models(cls) -> None:
        """Drop ONNX models from memory and return native allocations to the OS.

        Call after a large indexing job completes. Models reload in ~1.5s from
        the cached volume on the next embed request. Tokenizers stay loaded.
        """
        cls._shared_dense_model = None
        cls._shared_sparse_model = None
        trim_memory()
        _tlog.info("models_released memory_freed=true")

    @classmethod
    def start_idle_timer(cls, timeout_s: int) -> None:
        """Start a background asyncio task that releases models after idle.

        If timeout_s is 0 the timer is disabled. Safe to call multiple times —
        the previous task is cancelled before starting a new one.
        Also stores timeout_s so _ensure_idle_timer() can lazily re-start it.
        """
        cls._idle_timeout_s = timeout_s
        if timeout_s <= 0:
            return
        cls.stop_idle_timer()

        async def _idle_watcher() -> None:
            check_interval = min(60, timeout_s)
            while True:
                await asyncio.sleep(check_interval)
                if cls._shared_dense_model is None and cls._shared_sparse_model is None:
                    # Models already released; nothing to do.
                    continue
                idle_s = time.monotonic() - cls._last_embed_time
                if idle_s >= timeout_s:
                    _tlog.info(
                        "idle_timeout_releasing_models idle_s=%.0f timeout_s=%d",
                        idle_s, timeout_s,
                    )
                    cls.release_models()

        try:
            loop = asyncio.get_running_loop()
            cls._idle_timer = loop.create_task(_idle_watcher(), name="embedder-idle-timer")
        except RuntimeError:
            # No running event loop (e.g. during tests). Skip gracefully.
            pass

    @classmethod
    def _ensure_idle_timer(cls) -> None:
        """Lazily start the idle timer on the first embed call inside the event loop.

        Called from async embed methods so the asyncio loop is always available.
        No-op if the timer is already running, disabled (timeout=0), or no models
        are loaded.
        """
        if cls._idle_timeout_s <= 0:
            return
        if cls._idle_timer is not None and not cls._idle_timer.done():
            return
        # Timer not yet started or was cancelled — start it now.
        cls.start_idle_timer(cls._idle_timeout_s)

    @classmethod
    def stop_idle_timer(cls) -> None:
        """Cancel the idle-timeout watcher task if running."""
        if cls._idle_timer is not None and not cls._idle_timer.done():
            cls._idle_timer.cancel()
        cls._idle_timer = None

    def __init__(
        self,
        dense_model: str,
        sparse_model: str,
        dense_embed_vector_size: int,
        batch_size: int = 16,
        hybrid: bool = True,
        dense_threads: int = 0,
        sparse_threads: int = 0,
        max_dense_embed_tokens: int = 0,
        max_sparse_embed_tokens: int = 0,
        memory_warn_pct: int = 70,
        memory_halt_pct: int = 85,
        sequential_embed: bool = False,
    ):
        self.dense_model = dense_model
        self.sparse_model = sparse_model
        self.dense_embed_vector_size = dense_embed_vector_size
        self.batch_size = batch_size
        self.hybrid = hybrid
        self.dense_threads = dense_threads
        self.sparse_threads = sparse_threads
        self._max_dense_embed_tokens_cfg = max_dense_embed_tokens
        self._max_sparse_embed_tokens_cfg = max_sparse_embed_tokens
        self.memory_warn_pct = memory_warn_pct
        self.memory_halt_pct = memory_halt_pct
        self.sequential_embed = sequential_embed
        self._dense_truncation_ready = False
        self._sparse_truncation_ready = False

    @property
    def _dense_max_tokens(self) -> int:
        return Embedder._shared_dense_max_tokens

    @property
    def _sparse_max_tokens(self) -> int:
        return Embedder._shared_sparse_max_tokens

    def _ensure_dense_truncation(self) -> None:
        if self._dense_truncation_ready:
            return
        model = self._get_dense_model()
        Embedder._shared_dense_tokenizer = _extract_tokenizer(model)
        model_dir = _extract_model_dir(model)
        max_tok, source = resolve_max_embed_tokens(
            role="dense",
            model_name=self.dense_model,
            env_tokens=self._max_dense_embed_tokens_cfg,
            model_dir=model_dir,
            known_registry=KNOWN_EMBED_MODEL_MAX_TOKENS,
        )
        Embedder._shared_dense_max_tokens = max_tok
        Embedder._shared_dense_truncation_source = source
        self._dense_truncation_ready = True

    def _ensure_sparse_truncation(self) -> None:
        if self._sparse_truncation_ready:
            return
        model = self._get_sparse_model()
        Embedder._shared_sparse_tokenizer = _extract_tokenizer(model)
        model_dir = _extract_model_dir(model)
        max_tok, source = resolve_max_embed_tokens(
            role="sparse",
            model_name=self.sparse_model,
            env_tokens=self._max_sparse_embed_tokens_cfg,
            model_dir=model_dir,
            known_registry=KNOWN_EMBED_MODEL_MAX_TOKENS,
        )
        Embedder._shared_sparse_max_tokens = max_tok
        Embedder._shared_sparse_truncation_source = source
        self._sparse_truncation_ready = True

    def _get_dense_model(self):
        """Get or load fastembed dense encoder (ONNX, cached)."""
        if Embedder._shared_dense_model is None:
            from fastembed import TextEmbedding
            threads = _resolve_threads(self.dense_threads)
            _tlog.info("loading_dense_model model=%s threads=%d backend=fastembed-onnx", self.dense_model, threads)
            t0 = time.monotonic()
            # `threads` sets intra_op_num_threads on the ONNX InferenceSession directly.
            # OMP_NUM_THREADS alone is insufficient — ONNX Runtime uses its own thread pool.
            Embedder._shared_dense_model = TextEmbedding(model_name=self.dense_model, threads=threads)
            _tlog.info("dense_model_loaded model=%s threads=%d elapsed_s=%.2f", self.dense_model, threads, time.monotonic() - t0)
        return Embedder._shared_dense_model

    def _get_sparse_model(self):
        """Get or load sparse encoder (cached). Model is configurable via sparse_model."""
        if Embedder._shared_sparse_model is None:
            from fastembed.sparse import SparseTextEmbedding
            threads = _resolve_threads(self.sparse_threads)
            _tlog.info("loading_sparse_model model=%s threads=%d", self.sparse_model, threads)
            t0 = time.monotonic()
            Embedder._shared_sparse_model = SparseTextEmbedding(model_name=self.sparse_model, threads=threads)
            _tlog.info("sparse_model_loaded model=%s elapsed_s=%.2f", self.sparse_model, time.monotonic() - t0)
        return Embedder._shared_sparse_model

    def _truncate_dense(self, text: str) -> tuple[str, int]:
        self._ensure_dense_truncation()
        return truncate_for_embedding(
            text,
            max_tokens=self._dense_max_tokens,
            tokenizer=Embedder._shared_dense_tokenizer,
        )

    def _truncate_sparse(self, text: str) -> str:
        self._ensure_sparse_truncation()
        if self._sparse_max_tokens <= 0:
            return text
        truncated, _ = truncate_for_embedding(
            text,
            max_tokens=self._sparse_max_tokens,
            tokenizer=Embedder._shared_sparse_tokenizer,
        )
        return truncated

    def _adaptive_batch_size(
        self,
        max_tokens_in_batch: int,
        base_batch: int,
        pressure_pct: float = 0.0,
    ) -> int:
        """Reduce batch size for long sequences to limit ONNX attention memory.

        ONNX attention is O(seq_len² × batch_size), so batches with long
        sequences need fewer items to avoid memory spikes.

        When cgroup pressure is below memory_warn_pct, use gentler reductions
        so high-memory containers are not throttled unnecessarily.
        """
        if pressure_pct > 0 and pressure_pct < self.memory_warn_pct:
            if max_tokens_in_batch >= 512:
                return max(1, base_batch // 2)
            return base_batch
        if max_tokens_in_batch >= 512:
            return max(1, base_batch // 4)
        if max_tokens_in_batch >= 256:
            return max(1, base_batch // 2)
        return base_batch

    def _embed_dense_batch_sync(self, texts: list[str]) -> list[list[float]]:
        """Embed texts via fastembed ONNX (synchronous, CPU-bound).

        Texts are sorted by length before embedding so that each ONNX batch
        contains similar-length sequences.  ONNX pads every input in a batch
        to the longest one, and transformer attention is O(seq_len²), so
        mixing a 4 000-char chunk with several 200-char chunks forces the
        short ones through O(4000²) attention — ~400× wasted compute.
        Length-sorting keeps padding minimal, typically yielding 2-4× higher
        throughput with zero extra CPU or RAM.

        Under memory pressure the batch size is adaptively reduced and, at
        the halt threshold, embedding is aborted with an ``EmbeddingError``
        so the pipeline can surface a clear diagnostic instead of being
        OOM-killed silently.
        """
        model = self._get_dense_model()
        self._ensure_dense_truncation()

        truncated: list[str] = []
        token_lengths: list[int] = []
        for t in texts:
            tr, count = self._truncate_dense(t)
            truncated.append(tr)
            token_lengths.append(
                count if count >= 0 else max(1, len(tr) // 3)
            )
        if self._dense_max_tokens > 0:
            truncated_count = sum(
                1 for orig, tr in zip(texts, truncated) if len(orig) != len(tr)
            )
            if truncated_count:
                _tlog.warning(
                    "dense_chunks_truncated count=%d max_tokens=%d source=%s",
                    truncated_count,
                    self._dense_max_tokens,
                    Embedder._shared_dense_truncation_source,
                )
        char_lengths = [len(t) for t in truncated]
        total = len(truncated)

        rss_start = get_rss_mb()
        _tlog.info(
            "dense_embed_start chunks=%d batch_size=%d tokens_avg=%d tokens_max=%d "
            "chars_avg=%d chars_max=%d rss_mb=%.1f",
            total, self.batch_size, round(statistics.mean(token_lengths)),
            max(token_lengths), round(statistics.mean(char_lengths)),
            max(char_lengths), rss_start,
        )

        # Sort by token length so each ONNX batch has similar-length texts (less padding)
        sorted_indices = sorted(range(total), key=lambda i: token_lengths[i])
        sorted_texts = [truncated[i] for i in sorted_indices]
        sorted_token_lengths = [token_lengths[i] for i in sorted_indices]

        # Keep embeddings as numpy float32 arrays (not Python float lists).
        # A 768-dim list of Python floats is ~24 KB; the numpy array is ~3 KB,
        # an ~8x RAM saving across the held/double-buffered batch.
        sorted_embeddings: list = []
        t0 = time.monotonic()
        t_last_log = t0

        # Manually slice into sub-batches so we can adapt batch size per-slice
        # based on max sequence length and memory pressure.
        i = 0
        while i < len(sorted_texts):
            # Check memory pressure before each sub-batch
            severity, pct = check_memory_pressure(self.memory_warn_pct, self.memory_halt_pct)
            if severity == "halt":
                _tlog.error(
                    "dense_embed_halt memory_pct=%.1f halt_pct=%d done=%d total=%d",
                    pct, self.memory_halt_pct, len(sorted_embeddings), total,
                )
                raise EmbeddingError(
                    f"Memory pressure {pct:.0f}% exceeds halt threshold "
                    f"({self.memory_halt_pct}%). Reduce BATCH_SIZE, FLUSH_EVERY, "
                    f"or MAX_DENSE_EMBED_TOKENS / MAX_SPARSE_EMBED_TOKENS, or increase "
                    f"container memory."
                )
            if severity == "warn":
                emergency_trim()
                _tlog.warning(
                    "dense_embed_memory_warn memory_pct=%.1f warn_pct=%d — halving batch size",
                    pct, self.memory_warn_pct,
                )

            # Determine effective batch size for this slice (two-pass peek).
            # First pass: conservative estimate using the full base batch window.
            max_tokens_in_slice = sorted_token_lengths[
                min(i + self.batch_size - 1, len(sorted_token_lengths) - 1)
            ]
            effective_bs = self._adaptive_batch_size(
                max_tokens_in_slice, self.batch_size, pct,
            )
            # Second pass: re-check with the actual effective window.
            # Because sorted_token_lengths is ascending, the reduced window
            # may exclude the long-tail tokens that triggered the first
            # reduction.  Cap at first_bs so recovery never re-includes them.
            first_bs = effective_bs
            actual_max = sorted_token_lengths[
                min(i + effective_bs - 1, len(sorted_token_lengths) - 1)
            ]
            effective_bs = min(
                first_bs,
                self._adaptive_batch_size(actual_max, self.batch_size, pct),
            )
            if severity == "warn":
                effective_bs = max(1, effective_bs // 2)

            sub_texts = sorted_texts[i:i + effective_bs]
            for emb in model.embed(sub_texts, batch_size=effective_bs):
                sorted_embeddings.append(emb)

            i += effective_bs

            now = time.monotonic()
            if now - t_last_log >= 5.0:
                elapsed = now - t0
                done = len(sorted_embeddings)
                _tlog.info(
                    "dense_embed_progress done=%d total=%d pct=%d elapsed_s=%.1f "
                    "chunks_per_s=%.1f effective_bs=%d rss_mb=%.1f",
                    done, total, round(done / total * 100),
                    elapsed, done / elapsed if elapsed > 0 else 0,
                    effective_bs, get_rss_mb(),
                )
                t_last_log = now

        # Restore original order
        embeddings: list = [None] * total
        for sorted_pos, orig_idx in enumerate(sorted_indices):
            embeddings[orig_idx] = sorted_embeddings[sorted_pos]

        elapsed = round(time.monotonic() - t0, 2)
        rss_end = get_rss_mb()

        # Spot-check dimension on first and last embedding (not all N)
        for i in (0, len(embeddings) - 1):
            if len(embeddings[i]) != self.dense_embed_vector_size:
                raise EmbeddingError(
                    f"Embedding {i} dimension mismatch: "
                    f"expected {self.dense_embed_vector_size}, got {len(embeddings[i])}"
                )

        _tlog.info(
            "dense_embed_done chunks=%d elapsed_s=%.2f chunks_per_s=%.1f rss_mb=%.1f rss_delta_mb=%.1f",
            len(embeddings), elapsed, len(embeddings) / elapsed if elapsed > 0 else 0,
            rss_end, rss_end - rss_start,
        )

        return embeddings

    def _embed_sparse_batch_sync(self, texts: list[str]) -> list[SparseVector]:
        """Compute sparse vectors via fastembed (synchronous, CPU-bound)."""
        model = self._get_sparse_model()
        self._ensure_sparse_truncation()
        if self._sparse_max_tokens > 0:
            truncated = [self._truncate_sparse(t) for t in texts]
            truncated_count = sum(
                1 for orig, tr in zip(texts, truncated) if len(orig) != len(tr)
            )
            if truncated_count:
                _tlog.warning(
                    "sparse_chunks_truncated count=%d max_tokens=%d source=%s",
                    truncated_count,
                    self._sparse_max_tokens,
                    Embedder._shared_sparse_truncation_source,
                )
            texts = truncated
        _tlog.info("sparse_embed_start chunks=%d", len(texts))
        t0 = time.monotonic()
        result = [
            SparseVector(indices=r.indices.tolist(), values=r.values.tolist())
            for r in model.embed(texts)
        ]
        _tlog.info("sparse_embed_done chunks=%d elapsed_s=%.2f", len(result), time.monotonic() - t0)
        return result

    async def embed_batch_dense(self, texts: list[str]) -> list[list[float]]:
        """Embed texts via fastembed ONNX in a thread executor."""
        loop = asyncio.get_running_loop()
        return await loop.run_in_executor(None, self._embed_dense_batch_sync, texts)

    async def embed_query(
        self, text: str,
    ) -> tuple[list[float], SparseVector | None]:
        """Embed a single query string into (dense_vector, sparse_vector).

        The sparse vector is None when hybrid search is disabled. When enabled,
        the dense and sparse encoders run concurrently in separate
        thread-pool workers. This is the public entry point all query tools
        should use instead of reaching into the private batch helpers.
        """
        Embedder._last_embed_time = time.monotonic()
        Embedder._ensure_idle_timer()
        loop = asyncio.get_running_loop()
        if self.hybrid:
            dense_list, sparse_list = await asyncio.gather(
                loop.run_in_executor(None, self._embed_dense_batch_sync, [text]),
                loop.run_in_executor(None, self._embed_sparse_batch_sync, [text]),
            )
            return dense_list[0], sparse_list[0]
        dense_list = await loop.run_in_executor(
            None, self._embed_dense_batch_sync, [text]
        )
        return dense_list[0], None

    async def embed_queries(
        self, texts: list[str],
    ) -> list[tuple[list[float], SparseVector | None]]:
        """Embed multiple query strings in one batched pass.

        Far cheaper than calling embed_query in a loop: the dense encoder
        processes all texts in a single ONNX run (with length-sorted batching)
        and the sparse encoder runs once over the whole list.
        """
        Embedder._last_embed_time = time.monotonic()
        Embedder._ensure_idle_timer()
        loop = asyncio.get_running_loop()
        if self.hybrid:
            dense_list, sparse_list = await asyncio.gather(
                loop.run_in_executor(None, self._embed_dense_batch_sync, texts),
                loop.run_in_executor(None, self._embed_sparse_batch_sync, texts),
            )
        else:
            dense_list = await loop.run_in_executor(
                None, self._embed_dense_batch_sync, texts
            )
            sparse_list = [None] * len(texts)  # type: ignore[list-item]
        return list(zip(dense_list, sparse_list))

    async def embed_chunks(self, chunks: list[Chunk]) -> list[EmbeddedChunk]:
        """Embed chunks with dense and (optionally) sparse vectors.

        Dense and sparse encoders normally run concurrently in separate
        thread-pool workers. Under memory pressure, concurrency is disabled
        and they run sequentially to reduce peak allocations.
        """
        Embedder._last_embed_time = time.monotonic()
        Embedder._ensure_idle_timer()
        texts = [c.content for c in chunks]
        loop = asyncio.get_running_loop()

        t_start = time.monotonic()

        # Under memory pressure, run sequentially to avoid concurrent allocation spikes.
        severity, pct = check_memory_pressure(self.memory_warn_pct, self.memory_halt_pct)
        force_sequential = self.sequential_embed or severity in ("warn", "halt")

        if self.hybrid and not force_sequential:
            dense_vectors, sparse_vectors = await asyncio.gather(
                loop.run_in_executor(None, self._embed_dense_batch_sync, texts),
                loop.run_in_executor(None, self._embed_sparse_batch_sync, texts),
            )
        elif self.hybrid:
            reason = "sequential_embed" if self.sequential_embed else "memory_pressure"
            log.info(
                "embed_sequential_mode",
                reason=reason,
                pressure_pct=pct if reason == "memory_pressure" else None,
            )
            dense_vectors = await loop.run_in_executor(None, self._embed_dense_batch_sync, texts)
            sparse_vectors = await loop.run_in_executor(None, self._embed_sparse_batch_sync, texts)
        else:
            dense_vectors = await loop.run_in_executor(None, self._embed_dense_batch_sync, texts)
            sparse_vectors = [None] * len(chunks)  # type: ignore[list-item]

        log.info(
            "embed_chunks_complete",
            chunks=len(chunks),
            hybrid=self.hybrid,
            total_elapsed_s=round(time.monotonic() - t_start, 2),
        )

        return [
            EmbeddedChunk(chunk=chunk, dense_vector=dv, sparse_vector=sv)
            for chunk, dv, sv in zip(chunks, dense_vectors, sparse_vectors)
        ]
