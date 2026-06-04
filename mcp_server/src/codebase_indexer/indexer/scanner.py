# src/codebase_indexer/indexer/scanner.py
"""File discovery with .gitignore and .codeindexignore support.

File I/O runs in a background thread with a bounded readahead queue so the
async event loop is never blocked by filesystem reads.
"""

import asyncio
import hashlib
import logging
import os
from dataclasses import dataclass
from pathlib import Path
from typing import AsyncGenerator

import pathspec
import structlog

from codebase_indexer.indexer.languages import EXTENSION_LANGUAGE_MAP, FILENAME_LANGUAGE_MAP

log = structlog.get_logger()
# stdlib logger for sync methods running inside thread-pool workers.
# structlog can silently drop logs from threads; stdlib logging is guaranteed thread-safe.
_tlog = logging.getLogger(__name__)

# Default directories pruned during the walk: build artifacts, VCS, caches,
# migration history, and editor metadata that never carry indexable source.
# Deliberately excludes
# project-specific or content-bearing folders (e.g. "docs") — those belong in a
# per-project .codeindexignore. Override the default set via Settings.excluded_dirs.
EXCLUDED_DIRS = {
    "node_modules",
    ".git",
    "__pycache__",
    ".venv",
    "venv",
    "dist",
    "build",
    "target",
    "bin",
    "obj",
    ".gradle",
    ".mypy_cache",
    ".pytest_cache",
    ".ruff_cache",
    ".idea",
    ".vscode",
    "migrations",
}


@dataclass
class FileRecord:
    abs_path: str
    rel_path: str
    language: str
    content: str
    sha256_hash: str
    mtime: float = 0.0
    mtime_skipped: bool = False


def _is_binary(data: bytes) -> bool:
    """Check if data looks like a binary file (null bytes in first 512 bytes)."""
    return b"\x00" in data[:512]


def _load_ignore_spec(workspace: Path, filename: str) -> pathspec.PathSpec | None:
    """Load a .gitignore-style file and return a PathSpec."""
    ignore_file = workspace / filename
    if ignore_file.is_file():
        try:
            patterns = ignore_file.read_text(encoding="utf-8", errors="replace").splitlines()
            return pathspec.PathSpec.from_lines("gitwildmatch", patterns)
        except Exception as e:
            log.warning("ignore_parse_error", file=filename, error=str(e))
    return None


def _detect_language(path: Path) -> str | None:
    """Detect language from filename or extension (including compound suffixes)."""
    name = path.name
    if name in FILENAME_LANGUAGE_MAP:
        return FILENAME_LANGUAGE_MAP[name]

    full_suffix = "".join(path.suffixes).lower()
    if full_suffix:
        lang = EXTENSION_LANGUAGE_MAP.get(full_suffix)
        if lang is not None:
            return lang

    return EXTENSION_LANGUAGE_MAP.get(path.suffix.lower())


# Fallback readahead depth used only when scan_files is called without an
# explicit value. The pipeline always passes Settings.readahead_buffer; this
# mirrors that default for standalone/direct callers.
_DEFAULT_READAHEAD = 100


async def scan_files(
    workspace_path: str,
    sub_path: str = "/",
    existing_metadata: dict[str, dict] | None = None,
    readahead: int | None = None,
    excluded_dirs: set[str] | None = None,
) -> AsyncGenerator[FileRecord, None]:
    """Recursively walk workspace, yielding FileRecord for each supported file.

    All filesystem I/O (os.walk, stat, read_bytes, sha256) runs in a background
    thread with a bounded readahead queue so the event loop is never blocked.

    Args:
        existing_metadata: When provided ({rel_path: {"sha256": str, "mtime": float | None}}),
            files whose mtime matches the stored value are yielded with
            mtime_skipped=True and content not loaded, avoiding unnecessary
            reads and SHA-256 hashing on unchanged files.
        excluded_dirs: Directory names pruned during the walk. Defaults to the
            module-level EXCLUDED_DIRS when not supplied (standalone callers).
    """
    workspace = Path(workspace_path).resolve()
    scan_root = (workspace / sub_path.lstrip("/")).resolve()
    excluded = excluded_dirs if excluded_dirs is not None else EXCLUDED_DIRS

    # Containment guard: never let a crafted sub_path (e.g. "../../etc/...")
    # escape the mounted workspace. _normalize_path already reduces inputs to a
    # single path segment upstream, but defense-in-depth keeps the scanner safe
    # regardless of how it is called.
    if scan_root != workspace and workspace not in scan_root.parents:
        log.error(
            "scan_path_outside_workspace",
            workspace=str(workspace),
            path=str(scan_root),
        )
        return

    if not scan_root.is_dir():
        log.error("scan_path_not_found", path=str(scan_root))
        return

    queue: asyncio.Queue[FileRecord | None] = asyncio.Queue(
        maxsize=readahead or _DEFAULT_READAHEAD
    )
    loop = asyncio.get_running_loop()

    def _scan_sync() -> None:
        """Run all filesystem I/O in a thread-pool worker."""
        try:
            # Workspace-root ignore files apply patterns relative to the
            # workspace root (matched against the workspace-relative path).
            root_gitignore_spec = _load_ignore_spec(workspace, ".gitignore")
            root_codeindexignore_spec = _load_ignore_spec(workspace, ".codeindexignore")

            # Project-level ignore files (when indexing a subdirectory project).
            # These apply patterns relative to the project root, so they are
            # matched against the scan-root-relative path. Without this a
            # project's own .gitignore was silently ignored.
            proj_gitignore_spec = None
            proj_codeindexignore_spec = None
            if scan_root != workspace:
                proj_gitignore_spec = _load_ignore_spec(scan_root, ".gitignore")
                proj_codeindexignore_spec = _load_ignore_spec(scan_root, ".codeindexignore")

            for dirpath, dirnames, filenames in os.walk(scan_root):
                # Filter out excluded directories in-place
                dirnames[:] = [d for d in dirnames if d not in excluded]

                # Only index CI workflows under .github/, not AGENTS.md or other metadata
                try:
                    rel_dir = Path(dirpath).relative_to(scan_root).as_posix()
                except ValueError:
                    rel_dir = ""
                if rel_dir == ".github" or rel_dir.endswith("/.github"):
                    dirnames[:] = [d for d in dirnames if d == "workflows"]
                    continue

                for filename in filenames:
                    abs_path = Path(dirpath) / filename
                    try:
                        rel_path = abs_path.relative_to(workspace).as_posix()
                    except ValueError:
                        continue
                    try:
                        rel_to_scan = abs_path.relative_to(scan_root).as_posix()
                    except ValueError:
                        rel_to_scan = rel_path

                    # Check ignore rules (root-relative and project-relative)
                    if root_gitignore_spec and root_gitignore_spec.match_file(rel_path):
                        continue
                    if root_codeindexignore_spec and root_codeindexignore_spec.match_file(rel_path):
                        continue
                    if proj_gitignore_spec and proj_gitignore_spec.match_file(rel_to_scan):
                        continue
                    if proj_codeindexignore_spec and proj_codeindexignore_spec.match_file(rel_to_scan):
                        continue

                    language = _detect_language(abs_path)
                    if language is None:
                        continue

                    try:
                        stat = abs_path.stat()
                        mtime = stat.st_mtime
                    except (OSError, PermissionError) as e:
                        _tlog.warning("file_stat_error path=%s error=%s", rel_path, e)
                        continue

                    # mtime pre-filter: skip file read if mtime is unchanged
                    if existing_metadata:
                        stored = existing_metadata.get(rel_path)
                        if (
                            stored
                            and stored.get("mtime") is not None
                            and stored["mtime"] == mtime
                        ):
                            record = FileRecord(
                                abs_path=str(abs_path),
                                rel_path=rel_path,
                                language=language,
                                content="",
                                sha256_hash=stored["sha256"],
                                mtime=mtime,
                                mtime_skipped=True,
                            )
                            asyncio.run_coroutine_threadsafe(
                                queue.put(record), loop
                            ).result()
                            continue

                    try:
                        raw = abs_path.read_bytes()
                    except (OSError, PermissionError) as e:
                        _tlog.warning("file_read_error path=%s error=%s", rel_path, e)
                        continue

                    if _is_binary(raw):
                        continue

                    content = raw.decode("utf-8", errors="replace")
                    sha256_hash = hashlib.sha256(raw).hexdigest()

                    record = FileRecord(
                        abs_path=str(abs_path),
                        rel_path=rel_path,
                        language=language,
                        content=content,
                        sha256_hash=sha256_hash,
                        mtime=mtime,
                    )
                    asyncio.run_coroutine_threadsafe(
                        queue.put(record), loop
                    ).result()
        finally:
            # Always send sentinel so the consumer never hangs
            asyncio.run_coroutine_threadsafe(queue.put(None), loop).result()

    scan_future = loop.run_in_executor(None, _scan_sync)

    while True:
        record = await queue.get()
        if record is None:
            break
        yield record

    # Propagate any exceptions from the scanner thread
    await scan_future
