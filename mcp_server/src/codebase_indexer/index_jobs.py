# src/codebase_indexer/index_jobs.py
"""In-memory tracker for background indexing jobs."""

import asyncio
import time
from dataclasses import dataclass, field
from enum import Enum


class JobStatus(str, Enum):
    QUEUED = "queued"
    RUNNING = "running"
    DONE = "done"
    FAILED = "failed"
    CANCELLED = "cancelled"


@dataclass
class IndexJob:
    collection: str
    path: str
    status: JobStatus = JobStatus.QUEUED
    started_at: float = 0.0
    finished_at: float = 0.0
    total_files: int = 0
    indexed_files: int = 0
    skipped_files: int = 0
    total_chunks: int = 0
    errors: list[str] = field(default_factory=list)
    error_message: str = ""
    _cancel_event: asyncio.Event = field(default_factory=asyncio.Event, repr=False)
    _done_event: asyncio.Event = field(default_factory=asyncio.Event, repr=False)
    _task: asyncio.Task | None = field(default=None, repr=False)

    @property
    def is_cancel_requested(self) -> bool:
        return self._cancel_event.is_set()

    def request_cancel(self) -> None:
        self._cancel_event.set()

    @property
    def elapsed_seconds(self) -> float:
        if self.started_at == 0:
            return 0.0
        end = self.finished_at if self.finished_at else time.monotonic()
        return round(end - self.started_at, 2)

    def to_dict(self) -> dict:
        return {
            "collection": self.collection,
            "path": self.path,
            "status": self.status.value,
            "elapsed_seconds": self.elapsed_seconds,
            "total_files": self.total_files,
            "indexed_files": self.indexed_files,
            "skipped_files": self.skipped_files,
            "total_chunks": self.total_chunks,
            "errors": self.errors,
            "error_message": self.error_message,
        }


class IndexJobTracker:
    """Async-safe tracker for background indexing jobs.

    Keeps at most ``max_jobs`` entries. When the cap is exceeded, the oldest
    *terminal* (done/failed/cancelled) jobs are evicted first so an active job
    is never dropped, bounding memory over long-lived server uptime.
    """

    _TERMINAL = (JobStatus.DONE, JobStatus.FAILED, JobStatus.CANCELLED)

    def __init__(self, max_jobs: int = 100) -> None:
        self._jobs: dict[str, IndexJob] = {}
        self._lock = asyncio.Lock()
        self._max_jobs = max_jobs

    def _evict_locked(self) -> None:
        if len(self._jobs) <= self._max_jobs:
            return
        # dict preserves insertion order → iterate oldest-first.
        for coll in list(self._jobs.keys()):
            if len(self._jobs) <= self._max_jobs:
                break
            if self._jobs[coll].status in self._TERMINAL:
                del self._jobs[coll]

    async def start_job(self, collection: str, path: str) -> IndexJob:
        async with self._lock:
            job = IndexJob(collection=collection, path=path)
            self._jobs[collection] = job
            self._evict_locked()
            return job

    async def get_job(self, collection: str) -> IndexJob | None:
        async with self._lock:
            return self._jobs.get(collection)

    async def get_all_jobs(self) -> list[IndexJob]:
        async with self._lock:
            return list(self._jobs.values())

    async def is_running(self, collection: str) -> bool:
        async with self._lock:
            job = self._jobs.get(collection)
            return job is not None and job.status in (JobStatus.QUEUED, JobStatus.RUNNING)

    async def cancel_job(self, collection: str) -> IndexJob | None:
        """Request cancellation of a running job. Returns the job if found."""
        async with self._lock:
            job = self._jobs.get(collection)
            if job and job.status in (JobStatus.QUEUED, JobStatus.RUNNING):
                job.request_cancel()
                return job
            return None
