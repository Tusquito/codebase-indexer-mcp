"""Local SearchResult stand-in (Python MCP storage removed)."""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class SearchResult:
    chunk_id: str
    score: float
    rel_path: str = ""
    language: str = ""
    start_line: int = 0
    end_line: int = 0
    symbol_name: str | None = None
    symbol_type: str | None = None
    content: str = ""
    collection: str = ""
