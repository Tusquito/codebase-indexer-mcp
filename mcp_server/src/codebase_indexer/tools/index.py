# src/codebase_indexer/tools/index.py
"""MCP tool: index_codebase (async background indexing)"""

import asyncio
import os
import re
import time

import structlog
from fastmcp import FastMCP

from codebase_indexer.config import Settings
from codebase_indexer.index_jobs import IndexJobTracker, JobStatus
from codebase_indexer.indexer.pipeline import run_pipeline, IndexCancelled
from codebase_indexer.indexer.embedder import Embedder
from codebase_indexer.storage.qdrant import QdrantStorage

log = structlog.get_logger()


def _normalize_path(raw_path: str) -> str:
    """Convert a host path, workspace path, or bare name to a container path.

    Always extracts the last path component (project folder name) so that
    full host paths like ``C:\\Users\\me\\repos\\my-project`` resolve to
    ``/my-project`` inside the container.

    Examples:
        "C:\\Users\\me\\Documents\\Repositories\\myproject" → "/myproject"
        "C:/Users/me/Documents/Repositories/myproject"      → "/myproject"
        "/workspace/myproject"                                 → "/myproject"
        "/myproject"                                           → "/myproject"
        "myproject"                                            → "/myproject"
        "/"                                                    → "/"
    """
    p = raw_path.strip().replace("\\", "/")
    # Strip Windows drive letter prefix (e.g. C:/)
    p = re.sub(r"^[A-Za-z]:/", "/", p)
    # Strip leading /workspace prefix if present
    p = re.sub(r"^/workspace/", "/", p)
    parts = [seg for seg in p.split("/") if seg]
    if not parts:
        return "/"
    # Use only the last path component as the project folder
    return "/" + parts[-1]


def _derive_collection_name(workspace_path: str, sub_path: str) -> str:
    """Derive collection name from the root folder being indexed."""
    clean = sub_path.strip("/")
    if not clean:
        return os.path.basename(os.path.normpath(workspace_path))
    return clean.split("/")[0]


async def _run_index_job(
    job_tracker: IndexJobTracker,
    collection: str,
    settings: Settings,
    storage: QdrantStorage,
    path: str,
    force: bool,
) -> None:
    """Background task that performs the actual indexing."""
    job = await job_tracker.get_job(collection)
    if not job:
        return

    job.status = JobStatus.RUNNING
    job.started_at = time.monotonic()
    log.info("index_job_started", collection=collection, path=path)

    try:
        result = await run_pipeline(
            settings=settings,
            storage=storage,
            collection=collection,
            sub_path=path,
            force=force,
            cancel_event=job._cancel_event,
        )
        job.total_files = result.total_files
        job.indexed_files = result.indexed_files
        job.skipped_files = result.skipped_files
        job.total_chunks = result.total_chunks
        job.errors = result.errors
        job.status = JobStatus.DONE
        job.finished_at = time.monotonic()
        log.info(
            "index_job_done",
            collection=collection,
            files=result.indexed_files,
            chunks=result.total_chunks,
            elapsed=job.elapsed_seconds,
        )
    except IndexCancelled as e:
        job.status = JobStatus.CANCELLED
        job.error_message = str(e)
        job.finished_at = time.monotonic()
        Embedder.release_models()
        log.info("index_job_cancelled", collection=collection, elapsed=job.elapsed_seconds)
    except Exception as e:
        job.status = JobStatus.FAILED
        job.error_message = str(e)
        job.finished_at = time.monotonic()
        Embedder.release_models()
        log.error("index_job_failed", collection=collection, error=str(e))


def register_index_tool(
    mcp: FastMCP,
    settings: Settings,
    storage: QdrantStorage,
    job_tracker: IndexJobTracker,
) -> None:
    @mcp.tool(
        name="index_codebase",
        description=(
            "Index a project for semantic search. "
            "IMPORTANT: 'path' must be the project folder name (basename of "
            "the working directory). For example, if working in "
            "'C:\\Users\\me\\repos\\my-project', pass path='my-project'. "
            "Do NOT pass '/' — that indexes the entire workspace. "
            "The collection is automatically named after the folder. "
            "Use index_status to check progress."
        ),
    )
    async def index_codebase(
        path: str = "/",
        collection: str | None = None,
        force: bool = False,
    ) -> dict:
        # Normalize host paths to container-relative paths
        path = _normalize_path(path)

        # Guard: if path is "/" the user likely meant their current project,
        # not the entire workspace. Reject and ask for a specific folder.
        if path == "/":
            return {
                "error": "Please specify a project folder to index.",
                "hint": (
                    "Pass the project folder name as 'path'. For example: "
                    "index_codebase(path='my-project'). "
                    "The path should be the basename of your working directory."
                ),
            }

        if collection is None:
            collection = _derive_collection_name(settings.workspace_path, path)

        if await job_tracker.is_running(collection):
            job = await job_tracker.get_job(collection)
            return {
                "message": f"Indexing already in progress for '{collection}'",
                "status": job.to_dict() if job else {},
            }

        await job_tracker.start_job(collection, path)

        # Fire and forget — runs in the background
        asyncio.create_task(
            _run_index_job(job_tracker, collection, settings, storage, path, force)
        )

        return {
            "message": f"Indexing started for '{collection}' in the background.",
            "collection": collection,
            "path": path,
            "hint": "Use index_status to check progress.",
        }

    @mcp.tool(
        name="index_status",
        description=(
            "Check the status of indexing jobs. Returns status for all "
            "projects, or a specific one if 'collection' is provided. "
            "Status can be: queued, running, done, failed, cancelled."
        ),
    )
    async def index_status(
        collection: str | None = None,
    ) -> dict | list[dict]:
        if collection:
            job = await job_tracker.get_job(collection)
            if not job:
                return {"error": f"No indexing job found for '{collection}'."}
            return job.to_dict()

        jobs = await job_tracker.get_all_jobs()
        if not jobs:
            return {"message": "No indexing jobs found. Use index_codebase to start one."}
        return [j.to_dict() for j in jobs]

    @mcp.tool(
        name="stop_indexing",
        description=(
            "Stop an ongoing indexing job. The job will finish its current "
            "batch and then stop gracefully. Chunks already embedded and "
            "upserted are kept — only the remaining work is skipped. "
            "Use index_status to confirm the job has stopped."
        ),
    )
    async def stop_indexing(
        collection: str,
    ) -> dict:
        job = await job_tracker.cancel_job(collection)
        if not job:
            existing = await job_tracker.get_job(collection)
            if existing:
                return {
                    "error": f"Job '{collection}' is not running (status: {existing.status.value}).",
                    "status": existing.to_dict(),
                }
            return {"error": f"No indexing job found for '{collection}'."}

        return {
            "message": f"Cancellation requested for '{collection}'. The job will stop after the current batch.",
            "collection": collection,
            "hint": "Use index_status to confirm it has stopped.",
        }
