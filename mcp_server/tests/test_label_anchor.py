"""Unit tests for content-anchored label resolution (ADR 0026 Phase 1)."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks.label_anchor import (  # noqa: E402
    Anchor,
    PointIndex,
    PointRecord,
    parse_anchors,
    resolve_anchor,
    resolve_anchors,
)
from codebase_indexer.indexer.chunker import _make_chunk_id  # noqa: E402

COLLECTION = "proj"


def _rec(rel_path: str, symbol: str | None, line: int) -> PointRecord:
    return PointRecord(
        chunk_id=_make_chunk_id(f"{COLLECTION}/{rel_path}", line),
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
    assert res.chunk_id == _make_chunk_id("proj/src/a.py", 42)


def test_content_re_resolution_on_line_drift():
    """The 0021 defect: line moved but symbol is unchanged."""
    index = PointIndex([_rec("src/a.py", "foo", 50)])
    anchor = Anchor(rel_path="src/a.py", symbol="foo", line=42, grade=2)
    res = resolve_anchor(anchor, index, collection=COLLECTION)
    assert res.method == "content"
    assert res.resolved and res.drifted
    assert res.chunk_id == _make_chunk_id("proj/src/a.py", 50)


def test_nearest_line_tie_break_on_duplicate_symbol():
    index = PointIndex([
        _rec("src/a.py", "foo", 10),
        _rec("src/a.py", "foo", 90),
    ])
    anchor = Anchor(rel_path="src/a.py", symbol="foo", line=80, grade=1)
    res = resolve_anchor(anchor, index, collection=COLLECTION)
    assert res.method == "nearest_line"
    assert res.chunk_id == _make_chunk_id("proj/src/a.py", 90)


def test_basename_anchor_for_non_code_by_rel_path():
    index = PointIndex([_rec("docs/adr/0004.md", None, 1)])
    anchor = Anchor(rel_path="docs/adr/0004.md", symbol=None, line=1, grade=1)
    res = resolve_anchor(anchor, index, collection=COLLECTION)
    assert res.method == "chunk_id"  # line still valid → legacy hit first
    assert res.chunk_id == _make_chunk_id("proj/docs/adr/0004.md", 1)


def test_basename_anchor_when_line_drifted_no_symbol():
    index = PointIndex([_rec("docs/adr/0004.md", None, 5)])
    anchor = Anchor(rel_path="docs/adr/0004.md", symbol=None, line=1, grade=1)
    res = resolve_anchor(anchor, index, collection=COLLECTION)
    assert res.method == "basename"
    assert res.resolved and res.drifted
    assert res.chunk_id == _make_chunk_id("proj/docs/adr/0004.md", 5)


def test_wrong_symbol_falls_back_to_basename():
    index = PointIndex([_rec("src/a.py", "actual", 60)])
    anchor = Anchor(rel_path="src/a.py", symbol="wrong", line=42, grade=2)
    res = resolve_anchor(anchor, index, collection=COLLECTION)
    assert res.method == "basename"
    assert res.chunk_id == _make_chunk_id("proj/src/a.py", 60)


def test_unresolved_when_file_absent():
    index = PointIndex([_rec("src/a.py", "foo", 42)])
    anchor = Anchor(rel_path="src/missing.py", symbol="bar", line=1, grade=1)
    res = resolve_anchor(anchor, index, collection=COLLECTION)
    assert res.method == "unresolved"
    assert not res.resolved
    assert res.chunk_id is None


def test_resolve_anchors_builds_qrels_and_report():
    index = PointIndex([
        _rec("src/a.py", "foo", 50),
        _rec("src/b.py", "bar", 10),
    ])
    anchors = [
        Anchor(rel_path="src/a.py", symbol="foo", line=42, grade=2),  # drifted
        Anchor(rel_path="src/b.py", symbol="bar", line=10, grade=1),  # exact
        Anchor(rel_path="src/gone.py", symbol="x", line=1, grade=1),  # unresolved
    ]
    labels, report = resolve_anchors(anchors, index, collection=COLLECTION)
    assert labels[_make_chunk_id("proj/src/a.py", 50)] == 2
    assert labels[_make_chunk_id("proj/src/b.py", 10)] == 1
    assert report.total == 3
    assert report.resolved == 2
    assert report.drifted == 1
    assert report.unresolved == 1
    assert report.by_method["chunk_id"] == 1
    assert report.by_method["content"] == 1
    assert report.by_method["unresolved"] == 1


def test_resolve_anchors_keeps_max_grade_on_collision():
    index = PointIndex([_rec("src/a.py", "foo", 10)])
    anchors = [
        Anchor(rel_path="src/a.py", symbol="foo", line=10, grade=1),
        Anchor(rel_path="src/a.py", symbol="foo", line=10, grade=2),
    ]
    labels, _report = resolve_anchors(anchors, index, collection=COLLECTION)
    assert labels[_make_chunk_id("proj/src/a.py", 10)] == 2


def test_parse_anchors_from_golden_shape():
    raw = [
        {"rel_path": "src/a.py", "symbol": "foo", "line": 42, "grade": 2},
        {"rel_path": "docs/x.md", "line": 1, "grade": 1},
    ]
    anchors = parse_anchors(raw)
    assert anchors[0] == Anchor(rel_path="src/a.py", symbol="foo", line=42, grade=2)
    assert anchors[1] == Anchor(rel_path="docs/x.md", symbol=None, line=1, grade=1)


def test_parse_anchors_empty():
    assert parse_anchors(None) == []
    assert parse_anchors([]) == []
