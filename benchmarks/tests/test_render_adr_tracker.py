"""Unit tests for scripts/render_adr_tracker.py (ADR 0019 Phase 1)."""

from __future__ import annotations

import shutil
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from scripts.render_adr_tracker import (  # noqa: E402
    ValidationError,
    build_blocks,
    build_open_decisions,
    build_summary,
    load_tracker,
    main,
    render,
    splice,
    validate,
)

FIXTURES = Path(__file__).parent / "fixtures" / "adr_tracker"


def _copy_fixtures(dest: Path) -> Path:
    shutil.copytree(FIXTURES, dest)
    return dest


def test_summary_rows_match_expected():
    tracker, _ = load_tracker(FIXTURES)
    expected = (FIXTURES / "expected_summary.md").read_text(encoding="utf-8").strip()
    assert build_summary(tracker) == expected


def test_phase_logs_group_and_order_newest_first():
    tracker, _ = load_tracker(FIXTURES)
    blocks = build_blocks(tracker)
    logs = blocks["phase-logs"]
    # ADR 0001 groups before 0002.
    assert logs.index("### ADR 0001") < logs.index("### ADR 0002")
    # Within ADR 0001, the newer implementation event precedes the plan event.
    assert logs.index("2026-01-02 — implementation") < logs.index("2026-01-01 — plan")


def test_open_decisions_union_dedup():
    tracker, _ = load_tracker(FIXTURES)
    out = build_open_decisions(tracker)
    lines = [line for line in out.splitlines() if line.startswith("- ")]
    # A (plan), B (plan), C (impl); B and A duplicates removed.
    assert lines == [
        "- alpha: decision A",
        "- alpha: decision B",
        "- alpha: decision C",
    ]


def test_validation_rejects_missing_required_field(tmp_path):
    root = _copy_fixtures(tmp_path / "t")
    bad = root / "phases" / "0003-phase-1.yaml"
    bad.write_text('adr_id: "0003"\nphase_key: phase-1\n', encoding="utf-8")
    tracker, schema = load_tracker(root)
    with pytest.raises(ValidationError, match="missing required field"):
        validate(tracker, schema)


def test_validation_rejects_invalid_tracker_status(tmp_path):
    root = _copy_fixtures(tmp_path / "t")
    bad = root / "phases" / "0001-phase-1.yaml"
    text = bad.read_text(encoding="utf-8").replace("tracker_status: planned", "tracker_status: bogus")
    bad.write_text(text, encoding="utf-8")
    tracker, schema = load_tracker(root)
    with pytest.raises(ValidationError, match="invalid tracker_status"):
        validate(tracker, schema)


def test_validation_rejects_invalid_event(tmp_path):
    root = _copy_fixtures(tmp_path / "t")
    bad = root / "events" / "0002-phase-1-2026-01-03-merge.yaml"
    text = bad.read_text(encoding="utf-8").replace("event: merge", "event: bogus")
    bad.write_text(text, encoding="utf-8")
    tracker, schema = load_tracker(root)
    with pytest.raises(ValidationError, match="invalid event"):
        validate(tracker, schema)


def test_check_returns_nonzero_on_drift_and_zero_on_match(tmp_path):
    output = tmp_path / "tracker.md"
    # First render writes the file.
    assert main(["--tracker-dir", str(FIXTURES), "--output", str(output)]) == 0
    # A matching --check passes.
    assert main(["--tracker-dir", str(FIXTURES), "--output", str(output), "--check"]) == 0
    # Introduce drift inside a generated block (edits outside markers are preserved).
    drifted = output.read_text(encoding="utf-8").replace("alpha scope", "drifted scope")
    output.write_text(drifted, encoding="utf-8")
    assert main(["--tracker-dir", str(FIXTURES), "--output", str(output), "--check"]) == 1


def test_preamble_outside_markers_preserved():
    blocks = build_blocks(load_tracker(FIXTURES)[0])
    existing = (
        "# My tracker\n\nManual preamble text.\n\n"
        "<!-- BEGIN GENERATED:summary -->\nOLD\n<!-- END GENERATED:summary -->\n\n"
        "Trailing manual note.\n"
    )
    result = splice(existing, blocks)
    assert "Manual preamble text." in result
    assert "Trailing manual note." in result
    assert "OLD" not in result
    assert build_summary(load_tracker(FIXTURES)[0]) in result


def test_render_scaffolds_when_no_output_exists(tmp_path):
    output = tmp_path / "new.md"
    rendered = render(FIXTURES, output)
    assert "<!-- BEGIN GENERATED:summary -->" in rendered
    assert "<!-- BEGIN GENERATED:open-decisions -->" in rendered
