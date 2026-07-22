"""Deterministic chunk ID helper (parity with .NET ChunkId.FromPathAndLine)."""

from __future__ import annotations

import hashlib


def make_chunk_id(rel_path: str, start_line: int) -> str:
    """SHA-256 hex of ``{rel_path}:{start_line}`` (lowercase, no separators)."""
    raw = f"{rel_path}:{start_line}"
    return hashlib.sha256(raw.encode()).hexdigest()
