# src/codebase_indexer/indexer/pipeline.py
"""Orchestrates scan → chunk → embed (dense+sparse) → upsert pipeline.

Uses double-buffered flushing: while batch N is being upserted to Qdrant
(I/O-bound), batch N+1 is being embedded (CPU-bound). This overlaps the
two phases for ~30-40% throughput improvement without extra CPU or RAM.

Additional concurrency optimizations:
- File scanning runs in a background thread with readahead queue
- Modified/stale file deletions are batched to reduce Qdrant round-trips
- Dense + sparse embeddings run concurrently in separate thread workers
- mtime pre-filtering skips unchanged files without reading them
"""

import asyncio
import time
from dataclasses import dataclass, field

try:
    import resource  # Unix-only; absent on local Windows dev
except ImportError:
    resource = None

import structlog

from codebase_indexer.config import Settings
from codebase_indexer.indexer.scanner import scan_files
from codebase_indexer.indexer.chunker import chunk_file, Chunk
from codebase_indexer.indexer.embedder import Embedder, trim_memory
from codebase_indexer.storage.qdrant import QdrantStorage

log = structlog.get_logger()


def _rss_mb() -> float:
    """Current process max RSS in MB (Linux reports ru_maxrss in KB)."""
    if resource is None:
        return 0.0
    try:
        return round(resource.getrusage(resource.RUSAGE_SELF).ru_maxrss / 1024, 1)
    except Exception:
        return 0.0


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
) -> PipelineResult:
    """Run the full indexing pipeline."""
    start_time = time.monotonic()
    result = PipelineResult()

    coll = collection or settings.qdrant_collection
    await storage.ensure_collection(coll)

    embedder = Embedder(
        model=settings.embed_model,
        vector_size=settings.vector_size,
        batch_size=settings.batch_size,
        hybrid=settings.hybrid_search,
        dense_threads=settings.dense_threads,
        sparse_threads=settings.sparse_threads,
        max_embed_chars=settings.max_embed_chars,
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
                    lambda fr=file_record: chunk_file(
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
        stale_paths = list(set(existing_hashes.keys()) - scanned_paths)
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
        peak_rss_mb=_rss_mb(),
    )

    # Release ONNX models after indexing to reclaim native memory.
    # They reload in ~1.5s from the cached volume on the next request.
    Embedder.release_models()

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
    2. Embed current batch (CPU-bound, thread executor).
    3. Fire upsert as background task (I/O-bound) and return the task handle.

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

        # Step 2: embed (CPU-bound)
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
            rss_mb=_rss_mb(),
        )

        # Step 3: fire upsert as background task — awaited on next flush call
        async def _do_upsert():
            ut0 = time.monotonic()
            await storage.upsert_chunks(collection, embedded)
            log.info("upsert_complete", chunks=chunk_count, upsert_s=round(time.monotonic() - ut0, 2))

        task = asyncio.create_task(_do_upsert())

        # Return freed native allocations (Python + ONNX arenas) to the OS each
        # flush so long jobs don't accumulate unbounded RSS.
        trim_memory()

        return task

    except Exception as e:
        log.error("flush_error", error=str(e), chunk_count=len(chunks))
        result.errors.append(f"Batch embed/upsert error: {e}")
        return None
