# src/codebase_indexer/indexer/embedder.py
"""Dense (fastembed ONNX) + Sparse (BM25 via fastembed) embedding client."""

import asyncio
import ctypes
import gc
import logging
import statistics
import time
from dataclasses import dataclass

import structlog

from codebase_indexer.indexer.chunker import Chunk

log = structlog.get_logger()
# stdlib logger for sync methods running inside thread-pool workers.
# structlog can silently drop logs from threads; stdlib logging is guaranteed thread-safe.
_tlog = logging.getLogger(__name__)


class EmbeddingError(Exception):
    pass


@dataclass
class SparseVector:
    indices: list[int]
    values: list[float]


@dataclass
class EmbeddedChunk:
    chunk: Chunk
    dense_vector: list[float]
    sparse_vector: SparseVector | None


class Embedder:
    """Local embedder: fastembed ONNX for dense + BM25 for sparse vectors.

    Models are loaded once at startup and shared across all instances.
    No external services required — fully self-contained.
    """

    # nomic-embed-text-v1.5 has 8192 token context, but ONNX attention memory
    # scales as O(seq_len² × batch_size). At batch_size=8, seq_len>2000 tokens
    # (~4000 chars) causes OOM. Cap conservatively to keep peak memory safe.
    MAX_EMBED_CHARS = 4_096

    # Class-level model cache — loaded once, reused by all instances
    _shared_dense_model = None
    _shared_sparse_model = None

    @classmethod
    def release_models(cls) -> None:
        """Drop ONNX models from memory and return native allocations to the OS.

        Call after a large indexing job completes. Models reload in ~1.5s from
        the cached volume on the next embed request.
        """
        cls._shared_dense_model = None
        cls._shared_sparse_model = None
        gc.collect()
        try:
            ctypes.cdll.LoadLibrary("libc.so.6").malloc_trim(0)
        except Exception:
            pass
        _tlog.info("models_released memory_freed=true")

    def __init__(
        self,
        model: str = "nomic-ai/nomic-embed-text-v1.5",
        vector_size: int = 768,
        batch_size: int = 16,
        hybrid: bool = True,
    ):
        self.model = model
        self.vector_size = vector_size
        self.batch_size = batch_size
        self.hybrid = hybrid

    def _get_dense_model(self):
        """Get or load fastembed dense encoder (ONNX, cached)."""
        if Embedder._shared_dense_model is None:
            import os
            from fastembed import TextEmbedding
            threads = int(os.environ.get("OMP_NUM_THREADS", 8))
            _tlog.info("loading_dense_model model=%s threads=%d backend=fastembed-onnx", self.model, threads)
            t0 = time.monotonic()
            # `threads` sets intra_op_num_threads on the ONNX InferenceSession directly.
            # OMP_NUM_THREADS alone is insufficient — ONNX Runtime uses its own thread pool.
            Embedder._shared_dense_model = TextEmbedding(model_name=self.model, threads=threads)
            _tlog.info("dense_model_loaded model=%s threads=%d elapsed_s=%.2f", self.model, threads, time.monotonic() - t0)
        return Embedder._shared_dense_model

    def _get_sparse_model(self):
        """Get or load BM25 sparse encoder (cached)."""
        if Embedder._shared_sparse_model is None:
            from fastembed.sparse import SparseTextEmbedding
            _tlog.info("loading_sparse_model model=Qdrant/bm25")
            t0 = time.monotonic()
            Embedder._shared_sparse_model = SparseTextEmbedding(model_name="Qdrant/bm25")
            _tlog.info("sparse_model_loaded model=Qdrant/bm25 elapsed_s=%.2f", time.monotonic() - t0)
        return Embedder._shared_sparse_model

    def _truncate(self, text: str) -> str:
        if len(text) > self.MAX_EMBED_CHARS:
            return text[:self.MAX_EMBED_CHARS]
        return text

    def _embed_dense_batch_sync(self, texts: list[str]) -> list[list[float]]:
        """Embed texts via fastembed ONNX (synchronous, CPU-bound).

        Texts are sorted by length before embedding so that each ONNX batch
        contains similar-length sequences.  ONNX pads every input in a batch
        to the longest one, and transformer attention is O(seq_len²), so
        mixing a 4 000-char chunk with several 200-char chunks forces the
        short ones through O(4000²) attention — ~400× wasted compute.
        Length-sorting keeps padding minimal, typically yielding 2-4× higher
        throughput with zero extra CPU or RAM.
        """
        model = self._get_dense_model()

        truncated_count = sum(1 for t in texts if len(t) > self.MAX_EMBED_CHARS)
        if truncated_count:
            _tlog.warning("chunks_truncated count=%d max_chars=%d", truncated_count, self.MAX_EMBED_CHARS)

        truncated = [self._truncate(t) for t in texts]
        lengths = [len(t) for t in truncated]
        total = len(truncated)
        _tlog.info(
            "dense_embed_start chunks=%d batch_size=%d chars_avg=%d chars_max=%d chars_min=%d",
            total, self.batch_size, round(statistics.mean(lengths)), max(lengths), min(lengths),
        )

        # Sort by length so each ONNX batch has similar-length texts (less padding)
        sorted_indices = sorted(range(total), key=lambda i: lengths[i])
        sorted_texts = [truncated[i] for i in sorted_indices]

        sorted_embeddings: list[list[float]] = []
        t0 = time.monotonic()
        t_last_log = t0

        # Consume the generator batch-by-batch so we can emit progress every ~5s
        batch: list[list[float]] = []
        for emb in model.embed(sorted_texts, batch_size=self.batch_size):
            batch.append(emb.tolist())
            if len(batch) == self.batch_size:
                sorted_embeddings.extend(batch)
                now = time.monotonic()
                if now - t_last_log >= 5.0:
                    elapsed = now - t0
                    done = len(sorted_embeddings)
                    _tlog.info(
                        "dense_embed_progress done=%d total=%d pct=%d elapsed_s=%.1f chunks_per_s=%.1f",
                        done, total, round(done / total * 100),
                        elapsed, done / elapsed if elapsed > 0 else 0,
                    )
                    t_last_log = now
                batch = []
        sorted_embeddings.extend(batch)  # flush remaining partial batch

        # Restore original order
        embeddings: list[list[float]] = [[] for _ in range(total)]
        for sorted_pos, orig_idx in enumerate(sorted_indices):
            embeddings[orig_idx] = sorted_embeddings[sorted_pos]

        elapsed = round(time.monotonic() - t0, 2)

        # Spot-check dimension on first and last embedding (not all N)
        for i in (0, len(embeddings) - 1):
            if len(embeddings[i]) != self.vector_size:
                raise EmbeddingError(
                    f"Embedding {i} dimension mismatch: "
                    f"expected {self.vector_size}, got {len(embeddings[i])}"
                )

        _tlog.info(
            "dense_embed_done chunks=%d elapsed_s=%.2f chunks_per_s=%.1f",
            len(embeddings), elapsed, len(embeddings) / elapsed if elapsed > 0 else 0,
        )

        return embeddings

    def _embed_sparse_batch_sync(self, texts: list[str]) -> list[SparseVector]:
        """Compute BM25 sparse vectors (synchronous, CPU-bound)."""
        model = self._get_sparse_model()
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
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, self._embed_dense_batch_sync, texts)

    async def embed_chunks(self, chunks: list[Chunk]) -> list[EmbeddedChunk]:
        """Embed chunks with dense and (optionally) sparse vectors.

        Dense (ONNX) and sparse (BM25) run concurrently in separate thread-pool
        workers. BM25 is lightweight (~5% of dense time) so the overlap adds
        negligible CPU contention while hiding its latency entirely.
        """
        texts = [c.content for c in chunks]
        loop = asyncio.get_event_loop()

        t_start = time.monotonic()

        if self.hybrid:
            dense_vectors, sparse_vectors = await asyncio.gather(
                loop.run_in_executor(None, self._embed_dense_batch_sync, texts),
                loop.run_in_executor(None, self._embed_sparse_batch_sync, texts),
            )
        else:
            dense_vectors = await loop.run_in_executor(None, self._embed_dense_batch_sync, texts)
            sparse_vectors = [None] * len(chunks)

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


# Backward-compatible alias
OllamaEmbedder = Embedder
