#!/usr/bin/env python3
"""One-time migration of the hand-maintained ADR tracker into YAML.

ADR 0019 — Adopt YAML structured events for ADR implementation tracking (Phase 2).

Parses the historical ``docs/adr/IMPLEMENTATION_TRACKER.md`` and emits
schema-valid YAML under ``docs/adr/tracker/``:

* one ``phases/{adr_id}-{phase_key}.yaml`` per Summary-table row (~28), and
* one ``events/{adr_id}-{phase_key}-{date}-{event}.yaml`` per phase-log
  ``#### <date> — <event>`` block (~100),

so that ``scripts/render_adr_tracker.py`` can regenerate the marker-delimited
sections of the markdown tracker from YAML. Genuinely-open rows in the
"Open decisions queue" table are folded into event ``open_decisions`` bullets.

Usage::

    python scripts/migrate_tracker_to_yaml.py --dry-run   # list planned files
    python scripts/migrate_tracker_to_yaml.py             # write YAML files

The helper is idempotent (overwrites existing generated files). Per ADR 0019
it is a Phase-2-only artifact.

ARCHIVED (ADR 0019 Phase 3): the historical migration has landed and the render
check is green, so this one-time helper is retained here under ``scripts/archive/``
for provenance only. It is no longer part of the active pipeline.
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path
from typing import Any

import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SOURCE = REPO_ROOT / "docs" / "adr" / "IMPLEMENTATION_TRACKER.md"
DEFAULT_TRACKER_DIR = REPO_ROOT / "docs" / "adr" / "tracker"

EVENT_ENUM = {"prioritization", "plan", "implementation", "verification", "merge", "defer"}
STATUS_FOR_EVENT = {
    "prioritization": "candidate",
    "plan": "planned",
    "implementation": "implemented",
    "verification": "verified",
    "merge": "merged",
    "defer": "deferred",
}

_PIPE_SPLIT = re.compile(r"(?<!\\)\|")
_EVENT_HEADER = re.compile(r"^####\s+(\d{4}-\d{2}-\d{2})\s+[—-]\s+(.+?)\s*$")
_ADR_SECTION = re.compile(r"^###\s+ADR\s+(\d{4})\b")
_FIELD = re.compile(r"^-\s+\*\*(.+?):\*\*\s*(.*)$")


# --------------------------------------------------------------------------- #
# Helpers
# --------------------------------------------------------------------------- #


def _slugify_phase(text: str) -> str:
    """Derive a stable kebab-case ``phase_key`` from a phase/track label."""
    t = text.lower()
    t = re.sub(r"[`*]", "", t)
    # normalise a leading bare number ("2 — track 1", "3+") to "phase N".
    t = re.sub(r"^\s*(\d+\+?)\b", r"phase \1", t)

    tokens: list[str] = []
    for m in re.finditer(r"phase\s+(\d+\+?)|track\s+([a-z0-9]+)", t):
        if m.group(1) is not None:
            tokens.append("phase-" + m.group(1).replace("+", "-plus"))
        else:
            tokens.append("track-" + m.group(2))

    # de-dup while preserving order
    seen: set[str] = set()
    ordered = [tok for tok in tokens if not (tok in seen or seen.add(tok))]
    if ordered:
        return "-".join(ordered)

    # fallback: generic slug of the whole (short) label
    generic = re.sub(r"[^a-z0-9]+", "-", t).strip("-")
    return generic or "phase"


def _strip_pr_suffix(text: str) -> str:
    """Reduce a "Phase / PR" value to a bare phase title."""
    for sep in (" — [PR", " — bundled in [PR", "; branch", " ([PR", " — [PR #"):
        idx = text.find(sep)
        if idx != -1:
            text = text[:idx]
    return text.strip()


def _split_row(line: str) -> list[str]:
    parts = _PIPE_SPLIT.split(line.strip())
    # drop the leading/trailing empty cells around the outer pipes
    if parts and parts[0].strip() == "":
        parts = parts[1:]
    if parts and parts[-1].strip() == "":
        parts = parts[:-1]
    return [p.strip().replace(r"\|", "|") for p in parts]


def _section(text: str, start_heading: str, stop_headings: tuple[str, ...]) -> str:
    lines = text.splitlines()
    out: list[str] = []
    capturing = False
    for line in lines:
        if not capturing:
            if line.strip() == start_heading:
                capturing = True
            continue
        if any(line.strip() == h or line.startswith(h) for h in stop_headings):
            break
        out.append(line)
    return "\n".join(out)


def _list_from_cell(value: str) -> list[str] | None:
    v = value.strip()
    if v in {"", "—", "-"}:
        return None
    backticked = re.findall(r"`([^`]+)`", v)
    if backticked and v.replace(" ", "").replace(",", "").replace("`", "").isalnum() is False:
        # narrative that happens to contain paths: only treat as a path list
        # when the whole cell is a comma-separated run of code spans.
        stripped = re.sub(r"`[^`]+`", "", v)
        if re.fullmatch(r"[\s,]*", stripped):
            return backticked
    if backticked and re.fullmatch(r"(\s*`[^`]+`\s*,?)+", v):
        return backticked
    return [v]


def _test_debt_list(value: str) -> list[str] | None:
    v = value.strip()
    if v in {"", "—", "-"}:
        return None
    if "; " in v:
        return [p.strip() for p in v.split("; ") if p.strip()]
    return [v]


def _parse_changelog(value: str) -> tuple[dict[str, Any], bool]:
    raw = value.strip()
    low = raw.lower()
    update = low.startswith("yes")
    if "user-facing yes" in low:
        user_facing = True
    elif "user-facing no" in low:
        user_facing = False
    else:
        user_facing = update
    changelog: dict[str, Any] = {"update": update}
    note = ""
    if "—" in raw:
        note = raw.split("—", 1)[1].strip()
    elif "-" in raw and not update:
        pass
    if note:
        changelog["reason"] = note
    return changelog, user_facing


def _parse_git(value: str) -> dict[str, Any] | None:
    raw = value.strip()
    if raw in {"", "pending", "—"}:
        return None
    git: dict[str, Any] = {}
    url = re.search(r"\((https://github[^)\s]+)\)", raw)
    if url:
        git["pr_url"] = url.group(1)
    commit = re.findall(r"`([0-9a-f]{7,40})`", raw)
    if not commit:
        commit = re.findall(r"\b([0-9a-f]{40})\b", raw)
    if commit:
        git["commit"] = commit[-1]
    git["status"] = "merged" if "merged" in raw.lower() else "pending"
    return git or None


# --------------------------------------------------------------------------- #
# Parsers
# --------------------------------------------------------------------------- #


def parse_phase_files(text: str) -> list[dict[str, Any]]:
    body = _section(text, "## Summary", ("## ", "Superseded ["))
    phases: list[dict[str, Any]] = []
    seen: set[str] = set()
    for line in body.splitlines():
        if not line.lstrip().startswith("|"):
            continue
        if "---" in line or "ADR | Title" in line:
            continue
        cells = _split_row(line)
        if len(cells) < 7:
            continue
        adr_m = re.search(r"(\d{4})", cells[0])
        if not adr_m:
            continue
        adr_id = adr_m.group(1)
        phase_key = _slugify_phase(cells[3])
        key = f"{adr_id}-{phase_key}"
        if key in seen:
            phase_key = f"{phase_key}-b"
            key = f"{adr_id}-{phase_key}"
        seen.add(key)
        last_updated = cells[6] if re.match(r"\d{4}-\d{2}-\d{2}", cells[6]) else "2026-07-04"
        data: dict[str, Any] = {
            "adr_id": adr_id,
            "phase_key": phase_key,
            "title": cells[1],
            "tracker_status": cells[4].strip("`"),
            "chosen_scope": cells[5],
            "last_updated": last_updated,
        }
        if cells[2] and cells[2] != "—":
            data["adr_status_note"] = cells[2]
        phases.append(data)
    return phases


def parse_event_files(text: str) -> list[dict[str, Any]]:
    body = _section(text, "## Phase logs", ("## How to update",))
    lines = body.splitlines()
    events: list[dict[str, Any]] = []

    current_adr: str | None = None
    i = 0
    n = len(lines)
    while i < n:
        line = lines[i]
        sec = _ADR_SECTION.match(line)
        if sec:
            current_adr = sec.group(1)
            i += 1
            continue
        hm = _EVENT_HEADER.match(line)
        if hm and current_adr:
            date = hm.group(1)
            event = _normalise_event(hm.group(2))
            fields: dict[str, str] = {}
            i += 1
            while i < n:
                nxt = lines[i]
                if _EVENT_HEADER.match(nxt) or _ADR_SECTION.match(nxt) or nxt.strip() == "---":
                    break
                fm = _FIELD.match(nxt.strip())
                if fm:
                    fields[fm.group(1).strip().lower()] = fm.group(2).strip()
                i += 1
            events.append(_build_event(current_adr, date, event, fields))
            continue
        i += 1
    return events


def _normalise_event(name: str) -> str:
    low = name.lower().strip()
    first = re.match(r"([a-z]+)", low)
    if first and first.group(1) in EVENT_ENUM:
        return first.group(1)
    if "deliver" in low:
        return "merge"
    return "merge"


def _build_event(adr_id: str, date: str, event: str, fields: dict[str, str]) -> dict[str, Any]:
    phase_raw = fields.get("phase / pr", fields.get("phase", ""))
    phase_title = _strip_pr_suffix(phase_raw) if phase_raw else f"Phase ({event})"
    phase_key = _slugify_phase(phase_raw) if phase_raw else "phase"

    status = fields.get("tracker status", "").strip("`") or STATUS_FOR_EVENT.get(event, "merged")
    choices = fields.get("choices", "").strip() or "—"
    deviations = fields.get("deviations", "").strip() or "none"

    changelog, user_facing = _parse_changelog(fields.get("changelog", "no"))

    data: dict[str, Any] = {
        "adr_id": adr_id,
        "phase_key": phase_key,
        "phase_title": phase_title,
        "event": event,
        "date": date,
        "tracker_status": status,
        "choices": choices,
        "deviations": deviations,
    }

    code_evidence = _list_from_cell(fields.get("code evidence", ""))
    if code_evidence:
        data["code_evidence"] = code_evidence
    test_debt = _test_debt_list(fields.get("test debt", ""))
    if test_debt:
        data["test_debt"] = test_debt
    verify = fields.get("verify", "").strip()
    if verify and verify not in {"—", "-"}:
        data["verify"] = verify
    git = _parse_git(fields.get("git", ""))
    if git:
        data["git"] = git

    data["changelog"] = changelog
    data["user_facing"] = user_facing
    return data


def parse_open_decisions(text: str) -> dict[str, list[str]]:
    """Return genuinely-open decision bullets keyed by adr_id."""
    body = _section(text, "## Open decisions queue", ("## How to update", "---"))
    by_adr: dict[str, list[str]] = {}
    for line in body.splitlines():
        if not line.lstrip().startswith("|"):
            continue
        cells = _split_row(line)
        if len(cells) < 5 or cells[0] == "Date" or "---" in line:
            continue
        adr_m = re.search(r"(\d{4})", cells[1])
        if not adr_m:
            continue
        decision = cells[3]
        plain = re.sub(r"[`*]", "", decision).strip()
        if not re.match(r"open\b", plain, re.IGNORECASE):
            continue
        adr_id = adr_m.group(1)
        question = re.sub(r"[`*]", "", cells[2]).strip()
        bullet = f"{adr_id}: {question} — {plain}"
        by_adr.setdefault(adr_id, []).append(bullet)
    return by_adr


# --------------------------------------------------------------------------- #
# Emission
# --------------------------------------------------------------------------- #


def _dump(data: dict[str, Any]) -> str:
    return yaml.safe_dump(
        data, sort_keys=False, allow_unicode=True, width=4096, default_flow_style=False
    )


def build_plan(source: Path) -> tuple[list[tuple[str, dict[str, Any]]], list[tuple[str, dict[str, Any]]]]:
    text = source.read_text(encoding="utf-8")
    phases = parse_phase_files(text)
    events = parse_event_files(text)
    open_by_adr = parse_open_decisions(text)

    # Fold open-decision bullets onto the newest event per ADR so the render's
    # open-decisions block reproduces them (deduped across events).
    newest_event: dict[str, dict[str, Any]] = {}
    for ev in events:
        key = ev["adr_id"]
        prev = newest_event.get(key)
        if prev is None or (ev["date"], ev["event"]) > (prev["date"], prev["event"]):
            newest_event[key] = ev
    for adr_id, bullets in open_by_adr.items():
        target = newest_event.get(adr_id)
        if target is not None:
            target.setdefault("open_decisions", []).extend(bullets)

    phase_files = [(f"{p['adr_id']}-{p['phase_key']}.yaml", p) for p in phases]

    event_files: list[tuple[str, dict[str, Any]]] = []
    used: set[str] = set()
    for ev in events:
        base = f"{ev['adr_id']}-{ev['phase_key']}-{ev['date']}-{ev['event']}"
        name = f"{base}.yaml"
        suffix = 2
        while name in used:
            name = f"{base}-{suffix}.yaml"
            suffix += 1
        used.add(name)
        event_files.append((name, ev))
    return phase_files, event_files


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--tracker-dir", type=Path, default=DEFAULT_TRACKER_DIR)
    parser.add_argument("--dry-run", action="store_true", help="list planned files; write nothing")
    args = parser.parse_args(argv)

    phase_files, event_files = build_plan(args.source)

    print(f"planned: {len(phase_files)} phase file(s), {len(event_files)} event file(s)")

    if args.dry_run:
        for name, _ in phase_files:
            print(f"  phases/{name}")
        for name, _ in event_files:
            print(f"  events/{name}")
        return 0

    phases_dir = args.tracker_dir / "phases"
    events_dir = args.tracker_dir / "events"
    phases_dir.mkdir(parents=True, exist_ok=True)
    events_dir.mkdir(parents=True, exist_ok=True)

    for name, data in phase_files:
        (phases_dir / name).write_text(_dump(data), encoding="utf-8")
    for name, data in event_files:
        (events_dir / name).write_text(_dump(data), encoding="utf-8")

    print(f"wrote {len(phase_files)} phase + {len(event_files)} event files to {args.tracker_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
