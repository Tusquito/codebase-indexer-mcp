"""Unit tests for content-anchored label resolution (ADR 0026 Phase 1)."""

from __future__ import annotations

from benchmarks.chunk_id import make_chunk_id
from benchmarks.label_anchor import (
    Anchor,
    PointIndex,
    PointRecord,
    parse_anchors,
    resolve_anchor,
    resolve_anchors,
)

COLLECTION = "proj"


def _rec(rel_path: str, symbol: str | None, line: int) -> PointRecord:
    return PointRecord(
        chunk_id=make_chunk_id(f"{COLLECTION}/{rel_path}", line),
        rel_path=f"{COLLECTION}/{rel_path}",
        symbol_name=symbol,
        start_line=line,
    )


def test_legacy_chunk_id_hit_when_line_unchanged():
    index = PointIndex([_rec("src/a.py", "foo", 42)])
    anchor = Anchor(rel_path="src/a.py", symbol="foo", line=42, grade=2)
    res = resolve_anchor(anchor, index, collection=COLLECTION)
    assert res.method == "chunk_id"
    assert res.resolved and not res.drifted
    assert res.chunk_id == make_chunk_id("proj/src/a.py", 42)


def test_content_re_resolution_on_line_drift():
    index = PointIndex([_rec("src/a.py", "foo", 50)])
    anchor = Anchor(rel_path="src/a.py", symbol="foo", line=42, grade=2)
    res = resolve_anchor(anchor, index, collection=COLLECTION)
    assert res.method == "content"
    assert res.resolved and res.drifted
    assert res.chunk_id == make_chunk_id("proj/src/a.py", 50)


def test_nearest_line_tie_break_on_duplicate_symbol():
    index = PointIndex(
        [
            _rec("src/a.py", "foo", 10),
            _rec("src/a.py", "foo", 90),
        ]
    )
    anchor = Anchor(rel_path="src/a.py", symbol="foo", line=80, grade=1)
    res = resolve_anchor(anchor, index, collection=COLLECTION)
    assert res.method == "nearest_line"
    assert res.chunk_id == make_chunk_id("proj/src/a.py", 90)


def test_basename_fallback():
    index = PointIndex([_rec("pkg/readme.md", None, 3)])
    anchor = Anchor(rel_path="other/readme.md", line=1, grade=1)
    res = resolve_anchor(anchor, index, collection=COLLECTION)
    assert res.method == "basename"
    assert res.chunk_id == make_chunk_id("proj/pkg/readme.md", 3)


def test_unresolved_when_missing():
    index = PointIndex([_rec("src/a.py", "foo", 10)])
    anchor = Anchor(rel_path="src/missing.py", symbol="bar", line=1, grade=1)
    res = resolve_anchor(anchor, index, collection=COLLECTION)
    assert res.method == "unresolved"
    assert not res.resolved


def test_resolve_anchors_prefers_higher_grade():
    index = PointIndex([_rec("src/a.py", "foo", 10)])
    labels, report = resolve_anchors(
        [
            Anchor(rel_path="src/a.py", symbol="foo", line=10, grade=1),
            Anchor(rel_path="src/a.py", symbol="foo", line=10, grade=2),
        ],
        index,
        collection=COLLECTION,
    )
    cid = make_chunk_id("proj/src/a.py", 10)
    assert labels[cid] == 2
    assert report.resolved == 2


def test_parse_anchors():
    anchors = parse_anchors(
        [{"rel_path": "a.cs", "symbol": "Foo", "line": 3, "grade": 2}]
    )
    assert anchors[0].rel_path == "a.cs"
    assert anchors[0].symbol == "Foo"
    assert anchors[0].line == 3
    assert anchors[0].grade == 2
