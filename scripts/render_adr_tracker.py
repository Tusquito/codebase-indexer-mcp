#!/usr/bin/env python3
"""Render + validate the generated sections of the ADR implementation tracker.

ADR 0019 — Adopt YAML structured events for ADR implementation tracking (Phase 1).

Loads ``docs/adr/tracker/phases/*.yaml`` and ``docs/adr/tracker/events/*.yaml``,
validates them against ``docs/adr/tracker/schema.yaml``, and regenerates the
generated markdown blocks (summary table, active/upcoming, phase logs, open
decisions) between HTML-comment markers in a target markdown file, preserving
any manual preamble outside the markers.

Usage::

    python scripts/render_adr_tracker.py                 # render into --output
    python scripts/render_adr_tracker.py --validate-only  # validate YAML only
    python scripts/render_adr_tracker.py --check          # validate + diff (no write)

Exit codes: 0 = ok; 1 = validation failure or (with --check) render drift.
"""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import yaml

REPO_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_TRACKER_DIR = REPO_ROOT / "docs" / "adr" / "tracker"
DEFAULT_OUTPUT = REPO_ROOT / "docs" / "adr" / "IMPLEMENTATION_TRACKER.md"

# Generated block markers. Content between each BEGIN/END pair is replaced on
# render; everything else in the target file is preserved verbatim.
BLOCK_NAMES = ("summary", "active", "phase-logs", "open-decisions")


def _begin(name: str) -> str:
    return f"<!-- BEGIN GENERATED:{name} -->"


def _end(name: str) -> str:
    return f"<!-- END GENERATED:{name} -->"


# Statuses considered "active / upcoming" (not terminal).
ACTIVE_STATUSES = {"candidate", "planned", "in_progress", "implemented", "verified"}


class ValidationError(Exception):
    """Raised when a phase/event file violates the schema contract."""


@dataclass
class Schema:
    tracker_status: set[str]
    event: set[str]
    git_status: set[str]
    phase_required: list[str]
    phase_optional: list[str]
    event_required: list[str]
    event_optional: list[str]
    changelog_required: list[str]
    changelog_optional: list[str]
    git_optional: list[str]

    @classmethod
    def load(cls, schema_path: Path) -> Schema:
        data = yaml.safe_load(schema_path.read_text(encoding="utf-8")) or {}
        enums = data.get("enums", {})
        phase = data.get("phase_file", {})
        event = data.get("event_file", {})
        changelog = data.get("changelog", {})
        git = data.get("git", {})
        return cls(
            tracker_status=set(enums.get("tracker_status", [])),
            event=set(enums.get("event", [])),
            git_status=set(enums.get("git_status", [])),
            phase_required=list(phase.get("required", [])),
            phase_optional=list(phase.get("optional", [])),
            event_required=list(event.get("required", [])),
            event_optional=list(event.get("optional", [])),
            changelog_required=list(changelog.get("required", [])),
            changelog_optional=list(changelog.get("optional", [])),
            git_optional=list(git.get("optional", [])),
        )


@dataclass
class PhaseFile:
    path: Path
    data: dict[str, Any]

    @property
    def adr_id(self) -> str:
        return str(self.data["adr_id"])

    @property
    def phase_key(self) -> str:
        return str(self.data["phase_key"])


@dataclass
class EventFile:
    path: Path
    data: dict[str, Any]

    @property
    def adr_id(self) -> str:
        return str(self.data["adr_id"])

    @property
    def date(self) -> str:
        return str(self.data.get("date", ""))

    @property
    def event(self) -> str:
        return str(self.data.get("event", ""))


@dataclass
class Tracker:
    phases: list[PhaseFile] = field(default_factory=list)
    events: list[EventFile] = field(default_factory=list)


def _load_yaml_dir(directory: Path) -> list[tuple[Path, dict[str, Any]]]:
    out: list[tuple[Path, dict[str, Any]]] = []
    if not directory.is_dir():
        return out
    for path in sorted(directory.glob("*.yaml")):
        loaded = yaml.safe_load(path.read_text(encoding="utf-8"))
        if not isinstance(loaded, dict):
            raise ValidationError(f"{path}: expected a YAML mapping at the top level")
        out.append((path, loaded))
    return out


def _require_fields(path: Path, data: dict[str, Any], required: list[str]) -> None:
    missing = [f for f in required if f not in data or data[f] is None]
    if missing:
        raise ValidationError(f"{path}: missing required field(s): {', '.join(missing)}")


def _reject_unknown(
    path: Path, data: dict[str, Any], required: list[str], optional: list[str]
) -> None:
    allowed = set(required) | set(optional)
    unknown = [k for k in data if k not in allowed]
    if unknown:
        raise ValidationError(f"{path}: unknown field(s): {', '.join(sorted(unknown))}")


def validate(tracker: Tracker, schema: Schema) -> None:
    for phase in tracker.phases:
        _require_fields(phase.path, phase.data, schema.phase_required)
        _reject_unknown(phase.path, phase.data, schema.phase_required, schema.phase_optional)
        status = phase.data["tracker_status"]
        if status not in schema.tracker_status:
            raise ValidationError(
                f"{phase.path}: invalid tracker_status {status!r} "
                f"(allowed: {', '.join(sorted(schema.tracker_status))})"
            )

    for event in tracker.events:
        _require_fields(event.path, event.data, schema.event_required)
        _reject_unknown(event.path, event.data, schema.event_required, schema.event_optional)
        status = event.data["tracker_status"]
        if status not in schema.tracker_status:
            raise ValidationError(
                f"{event.path}: invalid tracker_status {status!r} "
                f"(allowed: {', '.join(sorted(schema.tracker_status))})"
            )
        ev = event.data["event"]
        if ev not in schema.event:
            raise ValidationError(
                f"{event.path}: invalid event {ev!r} "
                f"(allowed: {', '.join(sorted(schema.event))})"
            )
        changelog = event.data["changelog"]
        if not isinstance(changelog, dict):
            raise ValidationError(f"{event.path}: changelog must be a mapping")
        _require_fields(event.path, changelog, schema.changelog_required)
        _reject_unknown(
            event.path, changelog, schema.changelog_required, schema.changelog_optional
        )
        git = event.data.get("git")
        if git is not None:
            if not isinstance(git, dict):
                raise ValidationError(f"{event.path}: git must be a mapping")
            _reject_unknown(event.path, git, [], schema.git_optional)
            gstatus = git.get("status")
            if gstatus is not None and gstatus not in schema.git_status:
                raise ValidationError(
                    f"{event.path}: invalid git.status {gstatus!r} "
                    f"(allowed: {', '.join(sorted(schema.git_status))})"
                )


def load_tracker(tracker_dir: Path) -> tuple[Tracker, Schema]:
    schema = Schema.load(tracker_dir / "schema.yaml")
    tracker = Tracker()
    for path, data in _load_yaml_dir(tracker_dir / "phases"):
        tracker.phases.append(PhaseFile(path, data))
    for path, data in _load_yaml_dir(tracker_dir / "events"):
        tracker.events.append(EventFile(path, data))
    return tracker, schema


def _clean(text: Any) -> str:
    """Collapse folded-scalar whitespace into a single markdown-table-safe line."""
    return " ".join(str(text).split()).replace("|", r"\|")


# --------------------------------------------------------------------------- #
# Generated block builders
# --------------------------------------------------------------------------- #


def build_summary(tracker: Tracker) -> str:
    header = (
        "| ADR | Title | ADR status | Phase | Tracker | Chosen scope | Last updated |\n"
        "|-----|-------|------------|-------|---------|--------------|--------------|"
    )
    rows: list[str] = []
    for phase in sorted(tracker.phases, key=lambda p: (p.adr_id, p.phase_key)):
        d = phase.data
        rows.append(
            "| {adr} | {title} | {status_note} | {phase} | `{tracker}` | "
            "{scope} | {updated} |".format(
                adr=phase.adr_id,
                title=_clean(d["title"]),
                status_note=_clean(d.get("adr_status_note", "—")),
                phase=_clean(d["phase_key"]),
                tracker=d["tracker_status"],
                scope=_clean(d["chosen_scope"]),
                updated=_clean(d["last_updated"]),
            )
        )
    body = "\n".join(rows) if rows else "| _(none)_ | | | | | | |"
    return f"{header}\n{body}"


def build_active(tracker: Tracker) -> str:
    lines: list[str] = []
    for phase in sorted(tracker.phases, key=lambda p: (p.adr_id, p.phase_key)):
        if phase.data["tracker_status"] in ACTIVE_STATUSES:
            lines.append(
                f"- **{phase.adr_id}** {_clean(phase.data['title'])} "
                f"— `{phase.data['tracker_status']}`"
            )
    if not lines:
        return "_No active or upcoming phases._"
    return "\n".join(lines)


def build_phase_logs(tracker: Tracker) -> str:
    by_adr: dict[str, list[EventFile]] = {}
    for event in tracker.events:
        by_adr.setdefault(event.adr_id, []).append(event)

    sections: list[str] = []
    for adr_id in sorted(by_adr):
        events = sorted(by_adr[adr_id], key=lambda e: (e.date, e.event), reverse=True)
        title = _clean(events[0].data.get("phase_title", "")) or f"ADR {adr_id}"
        sections.append(f"### ADR {adr_id} — {title}")
        for event in events:
            sections.append(_render_event(event))
    if not sections:
        return "_No phase log entries._"
    return "\n\n".join(sections)


def _render_event(event: EventFile) -> str:
    d = event.data
    lines = [f"#### {event.date} — {event.event}"]
    lines.append(f"- **Phase:** {_clean(d.get('phase_title', d['phase_key']))}")
    lines.append(f"- **Tracker status:** `{d['tracker_status']}`")
    lines.append(f"- **Choices:** {_clean(d['choices'])}")
    lines.append(f"- **Deviations:** {_clean(d['deviations'])}")
    if d.get("code_evidence"):
        joined = ", ".join(f"`{_clean(p)}`" for p in d["code_evidence"])
        lines.append(f"- **Code evidence:** {joined}")
    if d.get("test_debt"):
        joined = "; ".join(_clean(t) for t in d["test_debt"])
        lines.append(f"- **Test debt:** {joined}")
    if d.get("verify"):
        lines.append(f"- **Verify:** {_clean(d['verify'])}")
    git = d.get("git")
    if isinstance(git, dict):
        parts: list[str] = []
        if git.get("pr_url"):
            parts.append(str(git["pr_url"]))
        if git.get("status"):
            parts.append(f"status: {git['status']}")
        if git.get("commit"):
            parts.append(f"commit: {git['commit']}")
        if parts:
            lines.append(f"- **Git:** {_clean(' — '.join(parts))}")
    changelog = d.get("changelog", {})
    if isinstance(changelog, dict):
        flag = "yes" if changelog.get("update") else "no"
        note = changelog.get("bullet") or changelog.get("reason")
        suffix = f" — {_clean(note)}" if note else ""
        lines.append(f"- **Changelog:** {flag}{suffix}")
    return "\n".join(lines)


def build_open_decisions(tracker: Tracker) -> str:
    seen: set[str] = set()
    ordered: list[str] = []
    for event in tracker.events:
        for decision in event.data.get("open_decisions") or []:
            key = _clean(decision)
            if key not in seen:
                seen.add(key)
                ordered.append(key)
    if not ordered:
        return "_No open decisions._"
    return "\n".join(f"- {d}" for d in ordered)


def build_blocks(tracker: Tracker) -> dict[str, str]:
    return {
        "summary": build_summary(tracker),
        "active": build_active(tracker),
        "phase-logs": build_phase_logs(tracker),
        "open-decisions": build_open_decisions(tracker),
    }


# --------------------------------------------------------------------------- #
# Marker splicing
# --------------------------------------------------------------------------- #


def _default_scaffold(blocks: dict[str, str]) -> str:
    titles = {
        "summary": "## Summary",
        "active": "## Active and upcoming work",
        "phase-logs": "## Phase logs",
        "open-decisions": "## Open decisions queue",
    }
    parts = ["# ADR implementation tracker", ""]
    for name in BLOCK_NAMES:
        parts.append(titles[name])
        parts.append("")
        parts.append(_begin(name))
        parts.append(blocks[name])
        parts.append(_end(name))
        parts.append("")
    return "\n".join(parts).rstrip() + "\n"


def splice(existing: str | None, blocks: dict[str, str]) -> str:
    """Replace marker-delimited blocks in ``existing``; scaffold if no markers."""
    if existing is None:
        return _default_scaffold(blocks)

    has_any = any(_begin(name) in existing for name in BLOCK_NAMES)
    if not has_any:
        # No markers to splice into: preserve the file as preamble and append.
        return existing.rstrip() + "\n\n" + _default_scaffold(blocks)

    result = existing
    for name in BLOCK_NAMES:
        begin, end = _begin(name), _end(name)
        if begin not in result:
            continue
        if end not in result:
            raise ValidationError(f"marker {begin} present without matching {end}")
        pre, rest = result.split(begin, 1)
        _, post = rest.split(end, 1)
        result = f"{pre}{begin}\n{blocks[name]}\n{end}{post}"
    return result


def render(tracker_dir: Path, output: Path) -> str:
    tracker, schema = load_tracker(tracker_dir)
    validate(tracker, schema)
    blocks = build_blocks(tracker)
    existing = output.read_text(encoding="utf-8") if output.exists() else None
    return splice(existing, blocks)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tracker-dir", type=Path, default=DEFAULT_TRACKER_DIR)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--check",
        action="store_true",
        help="validate + render-diff without writing; non-zero exit on drift",
    )
    parser.add_argument(
        "--validate-only",
        action="store_true",
        help="validate YAML files only; no rendering or writing",
    )
    args = parser.parse_args(argv)

    try:
        tracker, schema = load_tracker(args.tracker_dir)
        validate(tracker, schema)
    except (ValidationError, yaml.YAMLError) as exc:
        print(f"validation error: {exc}", file=sys.stderr)
        return 1

    if args.validate_only:
        print(
            f"ok: {len(tracker.phases)} phase file(s), "
            f"{len(tracker.events)} event file(s) valid"
        )
        return 0

    blocks = build_blocks(tracker)
    existing = args.output.read_text(encoding="utf-8") if args.output.exists() else None
    rendered = splice(existing, blocks)

    if args.check:
        if existing == rendered:
            print(f"ok: {args.output} is up to date")
            return 0
        print(f"drift: {args.output} does not match rendered output", file=sys.stderr)
        return 1

    args.output.write_text(rendered, encoding="utf-8")
    print(f"wrote {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
