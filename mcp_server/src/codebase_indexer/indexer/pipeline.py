# src/codebase_indexer/indexer/pipeline.py
"""Orchestrates scan → chunk → embed (Ollama dense + sparse BM25) → upsert pipeline.

Uses double-buffered flushing: while batch N is being upserted to Qdrant
(I/O-bound), batch N+1 is being embedded (Ollama HTTP + sparse BM25 in thread
workers). This overlaps the two phases for ~30-40% throughput improvement
without extra CPU or RAM.

Additional concurrency optimizations:
- File scanning runs in a background thread with readahead queue
- Modified/stale file deletions are batched to reduce Qdrant round-trips
- Dense + sparse embeddings run concurrently in separate thread workers
- mtime pre-filtering skips unchanged files without reading them
- Memory-pressure monitoring throttles or aborts before OOM kill
"""

import asyncio
import gc
import time
from dataclasses import dataclass, field

import structlog

from codebase_indexer.config import Settings
from codebase_indexer.indexer.scanner import scan_files
from codebase_indexer.indexer.chunker import chunk_file, Chunk
from codebase_indexer.indexer.backends.factory import create_backends, create_colbert_backend
from codebase_indexer.indexer.embedder import Embedder, EmbeddingError, trim_memory
from codebase_indexer.memory import check_memory_pressure, get_rss_mb
from codebase_indexer.storage.qdrant import QdrantStorage

log = structlog.get_logger()


@dataclass
class PipelineResult:
    total_files: int = 0
    indexed_files: int = 0
    skipped_files: int = 0
    total_chunks: int = 0
    elapsed_seconds: float = 0.0
    errors: list[str] = field(default_factory=list)


class IndexCancelled(Exception):
    """Raised when an indexing job is cancelled mid-flight."""


async def run_pipeline(
    settings: Settings,
    storage: QdrantStorage,
    collection: str | None = None,
    sub_path: str = "/",
    force: bool = False,
    cancel_event: asyncio.Event | None = None,
    result: PipelineResult | None = None,
) -> PipelineResult:
    """Run the full indexing pipeline.

    Args:
        result: Optional progress object to update in place. When provided,
            counters and errors are written to this instance and the same
            object is returned. Callers must not replace it with a new
            ``PipelineResult`` inside the pipeline.
    """
    start_time = time.monotonic()
    if result is None:
        result = PipelineResult()

    coll = collection or settings.qdrant_collection
    await storage.ensure_collection(coll, force=force)

    dense_backend, sparse_backend = create_backends(settings)
    colbert_backend = create_colbert_backend(settings) if settings.rerank_enabled else None
    embedder = Embedder(
        dense_backend=dense_backend,
        sparse_backend=sparse_backend,
        dense_embed_vector_size=settings.dense_embed_vector_size,
        batch_size=settings.batch_size,
        hybrid=settings.hybrid_search,
        memory_warn_pct=settings.memory_pressure_warn_pct,
        memory_halt_pct=settings.memory_pressure_halt_pct,
        sequential_embed=settings.sequential_embed,
        colbert_backend=colbert_backend,
        rerank=settings.rerank_enabled,
    )

    flush_every = settings.flush_every
    loop = asyncio.get_running_loop()

    # Always fetch existing file metadata so we can delete stale/modified chunks.
    # The force flag only controls whether we skip unchanged files — not whether
    # we know what was previously indexed.
    existing_metadata = await storage.get_file_metadata(coll)
    existing_hashes = {k: v["sha256"] for k, v in existing_metadata.items()}

    scanned_paths: set[str] = set()
    pending_chunks: list[Chunk] = []
    modified_paths: list[str] = []
    scan_start = time.monotonic()

    # Defer HNSW index building during the bulk upload so it doesn't compete
    # with embedding for CPU. Rebuilt in one pass when we resume in `finally`.
    await storage.set_indexing(coll, enabled=False)
    indexing_paused = True

    # Double-buffer state: while Qdrant ingests batch N (I/O-bound),
    # the CPU embeds batch N+1. At most 2 batches in memory at once.
    inflight_upsert: asyncio.Task | None = None

    try:
        async for file_record in scan_files(
            settings.workspace_path,
            sub_path,
            existing_metadata=existing_metadata if not force else None,
            readahead=settings.readahead_buffer,
            excluded_dirs=settings.excluded_dirs_set,
        ):
            result.total_files += 1
            scanned_paths.add(file_record.rel_path)

            # Check for cancellation between files
            if cancel_event and cancel_event.is_set():
                log.info("indexing_cancelled", collection=coll, files_scanned=result.total_files, chunks=result.total_chunks)
                raise IndexCancelled(f"Cancelled after scanning {result.total_files} files, {result.total_chunks} chunks embedded")

            # mtime-skipped files are unchanged — no read or hash needed
            if file_record.mtime_skipped:
                result.skipped_files += 1
                continue

            # Skip unchanged files (SHA-256 check for files that were read)
            if not force and existing_hashes.get(file_record.rel_path) == file_record.sha256_hash:
                result.skipped_files += 1
                continue

            result.indexed_files += 1
            log.info("indexing_file", path=file_record.rel_path, language=file_record.language)

            # Track modified files for batch deletion (not inline)
            if file_record.rel_path in existing_hashes:
                modified_paths.append(file_record.rel_path)

            try:
                # Tree-sitter parsing is CPU-bound; run it in a thread executor
                # so it doesn't block the event loop (lets scan/upsert overlap).
                chunks = await loop.run_in_executor(
                    None,
                    lambda fr=file_record: chunk_file(  # type: ignore[misc]
                        content=fr.content,
                        rel_path=fr.rel_path,
                        language=fr.language,
                        file_sha256=fr.sha256_hash,
                        max_chunk_lines=settings.max_chunk_lines,
                        chunk_overlap_lines=settings.chunk_overlap_lines,
                        file_mtime=fr.mtime,
                    ),
                )
                pending_chunks.extend(chunks)

            except Exception as e:
                error_msg = f"Error processing {file_record.rel_path}: {e}"
                log.error("indexing_error", path=file_record.rel_path, error=str(e))
                result.errors.append(error_msg)

            # Flush periodically to keep memory bounded
            if len(pending_chunks) >= flush_every:
                # Check for cancellation before expensive embed+upsert
                if cancel_event and cancel_event.is_set():
                    log.info("indexing_cancelled_before_flush", collection=coll, chunks_pending=len(pending_chunks), total_chunks=result.total_chunks)
                    raise IndexCancelled(f"Cancelled before flush at {result.total_chunks} chunks embedded")

                log.info("flushing_chunk_batch", count=len(pending_chunks), total_so_far=result.total_chunks)

                # Batch-delete old chunks for modified files before upserting new ones
                if modified_paths:
                    await storage.delete_by_paths(coll, modified_paths)
                    modified_paths = []

                inflight_upsert = await _flush_double_buffered(
                    pending_chunks, embedder, storage, coll, result, inflight_upsert,
                )
                pending_chunks = []

        log.info(
            "scan_complete",
            files=result.total_files,
            indexed=result.indexed_files,
            skipped=result.skipped_files,
            scan_s=round(time.monotonic() - scan_start, 2),
        )

        # Release metadata dicts now — they held one entry per previously-
        # indexed file and are no longer needed after the scan loop.  On a
        # 17k-vector collection this frees ~tens of MB of Python objects.
        stale_paths = list(set(existing_hashes.keys()) - scanned_paths)
        del existing_metadata, existing_hashes
        gc.collect()

        # Flush remaining chunks
        if pending_chunks:
            log.info("flushing_final_batch", count=len(pending_chunks))

            # Batch-delete remaining modified files before final upsert
            if modified_paths:
                await storage.delete_by_paths(coll, modified_paths)
                modified_paths = []

            inflight_upsert = await _flush_double_buffered(
                pending_chunks, embedder, storage, coll, result, inflight_upsert,
            )

        # Wait for the very last upsert to finish
        if inflight_upsert is not None:
            try:
                await inflight_upsert
            except Exception as e:
                log.error("final_upsert_error", error=str(e))
                result.errors.append(f"Final upsert error: {e}")

        # Batch-delete stale chunks (files that were removed) in one call
        if stale_paths:
            log.info("deleting_stale_files", count=len(stale_paths))
            await storage.delete_by_paths(coll, stale_paths)
    finally:
        # Always re-enable indexing so a deferred/cancelled job never leaves the
        # collection with HNSW construction permanently disabled.
        if indexing_paused:
            await storage.set_indexing(coll, enabled=True)

    result.elapsed_seconds = round(time.monotonic() - start_time, 2)
    log.info(
        "pipeline_complete",
        total_files=result.total_files,
        indexed=result.indexed_files,
        skipped=result.skipped_files,
        chunks=result.total_chunks,
        elapsed=result.elapsed_seconds,
        peak_rss_mb=get_rss_mb(),
    )

    # Release ONNX models and reclaim native memory.
    # On by default: frees ~300-500 MB immediately after indexing.
    # Models reload in ~1.5s from cache on the next search query.
    rss_before = get_rss_mb()
    if settings.release_models_after_index:
        Embedder.release_models()

    # Unconditional post-pipeline cleanup: reclaim transient pipeline
    # allocations (chunk lists, numpy arrays, text buffers) from glibc arenas
    # even when models are kept resident.
    del embedder
    gc.collect()
    trim_memory()
    log.info(
        "pipeline_memory_reclaimed",
        rss_before_mb=rss_before,
        rss_after_mb=get_rss_mb(),
        models_released=settings.release_models_after_index,
    )

    return result


async def _flush_double_buffered(
    chunks: list[Chunk],
    embedder: Embedder,
    storage: QdrantStorage,
    collection: str,
    result: PipelineResult,
    prev_upsert: asyncio.Task | None,
) -> asyncio.Task | None:
    """Embed chunks and overlap upsert with the next embedding round.

    1. Wait for previous upsert (if any) — ensures at most 2 batches in RAM.
    2. Check memory pressure — abort early if above halt threshold.
    3. Embed current batch (Ollama dense HTTP + sparse BM25 in thread executors).
    4. Fire upsert as background task (I/O-bound) and return the task handle.

    Returns the new in-flight upsert task for the caller to track.
    """
    try:
        # Step 1: wait for previous upsert to complete
        if prev_upsert is not None:
            try:
                await prev_upsert
            except Exception as e:
                log.error("prev_upsert_error", error=str(e))
                result.errors.append(f"Upsert error: {e}")

        # Step 2: pre-flight memory pressure check
        severity, pct = check_memory_pressure(
            embedder.memory_warn_pct, embedder.memory_halt_pct
        )
        if severity == "halt":
            msg = (
                f"Memory pressure {pct:.0f}% exceeds halt threshold "
                f"({embedder.memory_halt_pct}%) before embedding {len(chunks)} chunks. "
                f"Reduce BATCH_SIZE, FLUSH_EVERY, MAX_DENSE_EMBED_TOKENS, or MAX_SPARSE_EMBED_TOKENS, or increase "
                f"container memory."
            )
            log.error("flush_halt_memory_pressure", pressure_pct=pct, chunks=len(chunks))
            result.errors.append(msg)
            raise EmbeddingError(msg)

        # Step 3: embed (CPU or GPU)
        t0 = time.monotonic()
        embedded = await embedder.embed_chunks(chunks)
        t1 = time.monotonic()

        chunk_count = len(chunks)
        result.total_chunks += chunk_count

        log.info(
            "flush_embedded",
            chunks=chunk_count,
            total_indexed=result.total_chunks,
            embed_s=round(t1 - t0, 2),
            rss_mb=get_rss_mb(),
        )

        # Step 4: fire upsert as background task — awaited on next flush call.
        # trim_memory runs *after* the upsert completes so the embedded data
        # can be freed first (it stays alive until upsert finishes).
        async def _do_upsert():
            ut0 = time.monotonic()
            await storage.upsert_chunks(collection, embedded)
            log.info("upsert_complete", chunks=chunk_count, upsert_s=round(time.monotonic() - ut0, 2))
            trim_memory()

        task = asyncio.create_task(_do_upsert())

        return task

    except EmbeddingError:
        raise
    except Exception as e:
        log.error("flush_error", error=str(e), chunk_count=len(chunks))
        result.errors.append(f"Batch embed/upsert error: {e}")
        return None
