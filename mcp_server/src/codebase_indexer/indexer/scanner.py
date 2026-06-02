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

log = structlog.get_logger()
# stdlib logger for sync methods running inside thread-pool workers.
# structlog can silently drop logs from threads; stdlib logging is guaranteed thread-safe.
_tlog = logging.getLogger(__name__)

EXTENSION_LANGUAGE_MAP = {
    ".py": "python",
    ".js": "javascript",
    ".jsx": "javascript",
    ".ts": "typescript",
    ".tsx": "typescript",
    ".go": "go",
    ".rs": "rust",
    ".java": "java",
    ".c": "c",
    ".h": "c",
    ".cpp": "cpp",
    ".cc": "cpp",
    ".cxx": "cpp",
    ".hpp": "cpp",
    ".cs": "csharp",
    ".xml": "xml",
    ".xsd": "xml",
    ".xsl": "xml",
    ".xslt": "xml",
    ".wsdl": "xml",
    ".yml": "yaml",
    ".yaml": "yaml",
    ".json": "json",
    ".proto": "protobuf",
    ".sql": "sql",
    ".kt": "kotlin",
    ".kts": "kotlin",
    ".scala": "scala",
    ".rb": "ruby",
    ".php": "php",
    ".swift": "swift",
    ".dart": "dart",
    ".sh": "bash",
    ".bash": "bash",
    ".ps1": "powershell",
    ".md": "markdown",
}

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
    ".github",
    ".idea",
    ".vscode",
    "docs",
    "Ops"
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
    """Detect language from file extension."""
    return EXTENSION_LANGUAGE_MAP.get(path.suffix.lower())


# Readahead buffer size: how many file records can be queued ahead of the
# consumer. Larger buffer keeps the CPU pipeline fed during bursty I/O.
_READAHEAD_BUFFER = 100


async def scan_files(
    workspace_path: str,
    sub_path: str = "/",
    existing_metadata: dict[str, dict] | None = None,
) -> AsyncGenerator[FileRecord, None]:
    """Recursively walk workspace, yielding FileRecord for each supported file.

    All filesystem I/O (os.walk, stat, read_bytes, sha256) runs in a background
    thread with a bounded readahead queue so the event loop is never blocked.

    Args:
        existing_metadata: When provided ({rel_path: {"sha256": str, "mtime": float | None}}),
            files whose mtime matches the stored value are yielded with
            mtime_skipped=True and content not loaded, avoiding unnecessary
            reads and SHA-256 hashing on unchanged files.
    """
    workspace = Path(workspace_path)
    scan_root = workspace / sub_path.lstrip("/")

    if not scan_root.is_dir():
        log.error("scan_path_not_found", path=str(scan_root))
        return

    queue: asyncio.Queue[FileRecord | None] = asyncio.Queue(maxsize=_READAHEAD_BUFFER)
    loop = asyncio.get_event_loop()

    def _scan_sync() -> None:
        """Run all filesystem I/O in a thread-pool worker."""
        try:
            gitignore_spec = _load_ignore_spec(workspace, ".gitignore")
            codeindexignore_spec = _load_ignore_spec(workspace, ".codeindexignore")

            for dirpath, dirnames, filenames in os.walk(scan_root):
                # Filter out excluded directories in-place
                dirnames[:] = [d for d in dirnames if d not in EXCLUDED_DIRS]

                for filename in filenames:
                    abs_path = Path(dirpath) / filename
                    try:
                        rel_path = abs_path.relative_to(workspace).as_posix()
                    except ValueError:
                        continue

                    # Check ignore rules
                    if gitignore_spec and gitignore_spec.match_file(rel_path):
                        continue
                    if codeindexignore_spec and codeindexignore_spec.match_file(rel_path):
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
