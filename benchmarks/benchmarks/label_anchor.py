"""Content-anchored golden-set label resolution (ADR 0026 Phase 1).

The legacy golden set keyed relevance labels on ``{rel_path}:{start_line}``
aliases (ADR 0007). Line numbers drift as indexed code changes between
sessions, which silently scored later runs against stale ``chunk_id`` values
and produced the ~60pp Jina recall swing documented in ADR 0021.

This module anchors labels to **content** — the ``{rel_path}::{symbol_name}``
pair already carried on every indexed Qdrant point — with the cached
``start_line`` retained only as a hint. Resolution follows a fixed ladder:

1. **legacy chunk_id hit** — ``_make_chunk_id(rel_path, line)`` still present.
2. **content re-resolution on drift** — match live points by rel_path + symbol.
3. **nearest-line tie-break** — when a symbol repeats in a file, pick the
   candidate whose ``start_line`` is closest to the cached hint.
4. **basename anchor for non-code** — no symbol / no rel_path match: fall back
   to matching by file basename, nearest cached line.
5. **unresolved** — reported (never silently scored against a stale id).

The resolver is pure over an in-memory :class:`PointIndex` so it is unit
testable without live services; :func:`load_point_index` builds that index
from a live Qdrant collection.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import PurePosixPath
from typing import Any

from benchmarks.chunk_id import make_chunk_id as _make_chunk_id

RESOLUTION_METHODS = (
    "chunk_id",
    "content",
    "nearest_line",
    "basename",
    "unresolved",
)


@dataclass
class Anchor:
    """One content-anchored relevance label from the golden set."""

    rel_path: str
    grade: int
    symbol: str | None = None
    line: int | None = None

    @property
    def key(self) -> str:
        sym = self.symbol or "-"
        line = self.line if self.line is not None else "-"
        return f"{self.rel_path}::{sym}@{line}"


@dataclass
class PointRecord:
    """Minimal indexed-point view needed for anchor resolution."""

    chunk_id: str
    rel_path: str
    symbol_name: str | None
    start_line: int


@dataclass
class ResolvedAnchor:
    anchor: Anchor
    chunk_id: str | None
    method: str

    @property
    def resolved(self) -> bool:
        return self.chunk_id is not None and self.method != "unresolved"

    @property
    def drifted(self) -> bool:
        """Resolved, but not by the cached line's legacy chunk_id."""
        return self.resolved and self.method != "chunk_id"


@dataclass
class DriftReport:
    total: int = 0
    resolved: int = 0
    drifted: int = 0
    unresolved: int = 0
    by_method: dict[str, int] = field(default_factory=dict)
    unresolved_keys: list[str] = field(default_factory=list)

    def record(self, res: ResolvedAnchor) -> None:
        self.total += 1
        self.by_method[res.method] = self.by_method.get(res.method, 0) + 1
        if res.resolved:
            self.resolved += 1
            if res.drifted:
                self.drifted += 1
        else:
            self.unresolved += 1
            self.unresolved_keys.append(res.anchor.key)

    def as_dict(self) -> dict[str, Any]:
        return {
            "total": self.total,
            "resolved": self.resolved,
            "drifted": self.drifted,
            "unresolved": self.unresolved,
            "by_method": dict(sorted(self.by_method.items())),
            "unresolved_keys": self.unresolved_keys,
        }


class PointIndex:
    """In-memory lookup over a collection's indexed points."""

    def __init__(self, records: list[PointRecord]):
        self._chunk_ids: set[str] = set()
        self._by_rel_symbol: dict[tuple[str, str], list[PointRecord]] = {}
        self._by_rel_path: dict[str, list[PointRecord]] = {}
        self._by_basename: dict[str, list[PointRecord]] = {}
        for rec in records:
            self._chunk_ids.add(rec.chunk_id)
            self._by_rel_path.setdefault(rec.rel_path, []).append(rec)
            base = PurePosixPath(rec.rel_path).name
            self._by_basename.setdefault(base, []).append(rec)
            if rec.symbol_name:
                self._by_rel_symbol.setdefault(
                    (rec.rel_path, rec.symbol_name), []
                ).append(rec)

    def has_chunk_id(self, chunk_id: str) -> bool:
        return chunk_id in self._chunk_ids

    def by_rel_symbol(self, rel_path: str, symbol: str) -> list[PointRecord]:
        return self._by_rel_symbol.get((rel_path, symbol), [])

    def by_rel_path(self, rel_path: str) -> list[PointRecord]:
        return self._by_rel_path.get(rel_path, [])

    def by_basename(self, basename: str) -> list[PointRecord]:
        return self._by_basename.get(basename, [])


def _prefixed(collection: str, rel_path: str) -> str:
    """Indexed ``rel_path`` payloads include a ``{collection}/`` folder prefix."""
    prefix = f"{collection}/"
    if rel_path.startswith(prefix):
        return rel_path
    return prefix + rel_path.lstrip("/")


def _nearest(records: list[PointRecord], line: int | None) -> PointRecord:
    if line is None or len(records) == 1:
        return records[0]
    return min(records, key=lambda r: abs(r.start_line - line))


def resolve_anchor(
    anchor: Anchor,
    index: PointIndex,
    *,
    collection: str,
) -> ResolvedAnchor:
    """Resolve one anchor to a live ``chunk_id`` via the ADR 0026 ladder."""
    rel_path = _prefixed(collection, anchor.rel_path)

    # 1. Legacy chunk_id hit — cached line still points at a live chunk.
    if anchor.line is not None:
        legacy = _make_chunk_id(rel_path, anchor.line)
        if index.has_chunk_id(legacy):
            return ResolvedAnchor(anchor, legacy, "chunk_id")

    # 2/3. Content re-resolution by symbol, nearest-line tie-break on collision.
    if anchor.symbol:
        candidates = index.by_rel_symbol(rel_path, anchor.symbol)
        if candidates:
            method = "content" if len(candidates) == 1 else "nearest_line"
            return ResolvedAnchor(anchor, _nearest(candidates, anchor.line).chunk_id, method)

    # 4. Basename anchor for non-code (no symbol, or file moved): match by
    #    rel_path first, then by bare basename, nearest cached line.
    path_candidates = index.by_rel_path(rel_path)
    if path_candidates:
        return ResolvedAnchor(anchor, _nearest(path_candidates, anchor.line).chunk_id, "basename")

    base = PurePosixPath(anchor.rel_path).name
    base_candidates = index.by_basename(base)
    if base_candidates:
        return ResolvedAnchor(anchor, _nearest(base_candidates, anchor.line).chunk_id, "basename")

    # 5. Unresolved — reported, never silently scored against a stale id.
    return ResolvedAnchor(anchor, None, "unresolved")


def resolve_anchors(
    anchors: list[Anchor],
    index: PointIndex,
    *,
    collection: str,
) -> tuple[dict[str, int], DriftReport]:
    """Resolve a set of anchors into a ``{chunk_id: grade}`` qrels dict.

    Higher grade wins when two anchors resolve to the same live chunk.
    """
    labels: dict[str, int] = {}
    report = DriftReport()
    for anchor in anchors:
        res = resolve_anchor(anchor, index, collection=collection)
        report.record(res)
        if res.chunk_id is not None:
            prior = labels.get(res.chunk_id)
            labels[res.chunk_id] = anchor.grade if prior is None else max(prior, anchor.grade)
    return labels, report


async def load_point_index(client: Any, collection: str) -> PointIndex:
    """Scroll a live Qdrant collection into an in-memory :class:`PointIndex`."""
    records: list[PointRecord] = []
    offset = None
    while True:
        points, offset = await client.scroll(
            collection_name=collection,
            limit=5000,
            offset=offset,
            with_payload=["chunk_id", "rel_path", "symbol_name", "start_line"],
            with_vectors=False,
        )
        for p in points:
            payload = p.payload or {}
            cid = payload.get("chunk_id")
            if not cid:
                continue
            records.append(
                PointRecord(
                    chunk_id=str(cid),
                    rel_path=str(payload.get("rel_path", "")),
                    symbol_name=payload.get("symbol_name"),
                    start_line=int(payload.get("start_line", 0) or 0),
                )
            )
        if offset is None:
            break
    return PointIndex(records)


def parse_anchors(raw: Any) -> list[Anchor]:
    """Parse the golden-set ``anchors`` field into :class:`Anchor` objects.

    Accepts a list of objects: ``{"rel_path", "grade", "symbol"?, "line"?}``.
    """
    anchors: list[Anchor] = []
    for item in raw or []:
        if "rel_path" not in item:
            raise ValueError(f"anchor missing rel_path: {item!r}")
        line = item.get("line")
        anchors.append(
            Anchor(
                rel_path=str(item["rel_path"]),
                grade=int(item.get("grade", 1)),
                symbol=(str(item["symbol"]) if item.get("symbol") else None),
                line=(int(line) if line is not None else None),
            )
        )
    return anchors
