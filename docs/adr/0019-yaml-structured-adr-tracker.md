# 0019. Adopt YAML structured events for ADR implementation tracking

- **Status:** Accepted (phase 3)
- **Date:** 2026-07-03
- **Deciders:** Maintainers
- **Related:** [`IMPLEMENTATION_TRACKER.md`](IMPLEMENTATION_TRACKER.md), [`.cursor/agents/adr-tracker.md`](../../.cursor/agents/adr-tracker.md), [ADR README](README.md)

## Context

ADR **decision records** (`docs/adr/NNNN-*.md`) and the **status index** (`docs/adr/README.md`) work well as markdown in git: human-readable, PR-reviewable, and linked from `ARCHITECTURE.md`.

**Execution tracking** lives in [`IMPLEMENTATION_TRACKER.md`](IMPLEMENTATION_TRACKER.md) — a single append-heavy markdown file (~1k+ lines) with:

- A summary table (multiple rows per ADR phase)
- Per-ADR phase logs (newest-first narrative blocks)
- An open-decisions queue

The ADR agent pipeline (`adr-tracker`, `adr-orchestrator`, …) updates this file by **string surgery** on large tables and sections. As ADR count and parallel phases grow, this creates:

| Gap | Impact |
|-----|--------|
| Merge conflicts | Multiple branches append to the same markdown file |
| No structured queries | Hard to ask “all open test debt” or “phases `in_progress`” without parsing prose |
| Fragile agent edits | Table row alignment and section headers are error-prone |
| Duplicated status | Summary table mirrors partial state also in README index rows |

We considered SQLite for tracker metadata. That adds runtime dependency, poor git diffs for binary `.db` files, and operational overhead unrelated to the MCP server’s retrieval mission. **YAML event files** offer structured data with git-native diffs and no new runtime service.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Tracker data model and file layout | yes | Schema for phases and events |
| Human-readable summary export | yes | Generated markdown from YAML |
| ADR decision bodies | no | Stay markdown in git |
| MCP server runtime | no | Tracker is maintainer/agent tooling only |
| SQLite / external DB | no | Deferred unless query scale demands it |

## Decision

We will store ADR **implementation tracking** as **versioned YAML files** under `docs/adr/tracker/`, with a small Python render script that regenerates the human-readable portions of `IMPLEMENTATION_TRACKER.md`.

ADR **decision documents** and the **README status index** remain hand-edited markdown. Only execution metadata moves to YAML.

### Directory layout

```
docs/adr/tracker/
  schema.yaml              # JSON Schema or documented field contract (reference)
  phases/
    0008-phase-2b.yaml     # one file per ADR phase (current state snapshot)
  events/
    0008-phase-2b-2026-07-03-merge.yaml   # one file per pipeline event (append-only history)
```

**Phase files** hold the latest snapshot for a delivery unit (one ADR phase = one PR):

- `adr_id`, `phase_key`, `title`
- `tracker_status` (`not_started` | `candidate` | `planned` | `in_progress` | `implemented` | `verified` | `merged` | `deferred`)
- `chosen_scope`, `last_updated`
- optional `pr_url`, `adr_status_note` (e.g. “Accepted (phase 1)”)

**Event files** record each pipeline step (plan, implementation, verification, merge, deferral). Filename convention: `{adr_id}-{phase_key}-{date}-{event}.yaml` (kebab-case, unique per event).

### Event file schema (required fields)

| Field | Type | Notes |
|-------|------|-------|
| `adr_id` | string | Four-digit id, e.g. `"0008"` |
| `phase_key` | string | Stable slug, e.g. `phase-2b` |
| `event` | string | `prioritization` \| `plan` \| `implementation` \| `verification` \| `merge` \| `defer` |
| `date` | string | ISO date `YYYY-MM-DD` |
| `tracker_status` | string | Status after this event |
| `choices` | string | Implementation/runtime choices |
| `deviations` | string | `none` or narrative |
| `changelog` | object | `{ update: bool, bullet?: string, reason?: string }` |
| `user_facing` | bool | Drives CHANGELOG rules (unchanged from today) |

### Event file schema (optional fields)

| Field | Type | Notes |
|-------|------|-------|
| `phase_title` | string | Human label; copied to phase file on first event |
| `code_evidence` | list[string] | Paths or grep hints |
| `test_debt` | list[string] | Carried-forward items |
| `verify` | string | Test agent summary |
| `git` | object | `{ pr_url?, commit?, status: pending \| merged }` |
| `open_decisions` | list[string] | Appended to open-decisions queue on render |

### Example event file

```yaml
adr_id: "0008"
phase_key: phase-2b
phase_title: "Phase 2 — track 2b (per-tool rerank=false override)"
event: merge
date: 2026-07-03
tracker_status: merged
choices: >
  Squash merge PR #7; final ADR 0008 phase complete; release skipped.
deviations: none
code_evidence:
  - mcp_server/src/codebase_indexer/tools/search_common.py
test_debt:
  - direct Embedder rerank unit tests
  - golden-set rerank=false quality sweep
verify: 23 targeted + 264 unit tests pass; plan compliance pass
git:
  status: merged
  pr_url: https://github.com/Tusquito/codebase-indexer-mcp/pull/7
  commit: 00f4c3e4fcc3efe4d81936e6025dab41d05e08f9
changelog:
  update: false
  reason: release skipped; Unreleased bullet retained from verification
user_facing: false
```

### Render script

Add `scripts/render_adr_tracker.py` (stdlib + PyYAML only):

1. Load all `phases/*.yaml` and `events/*.yaml`
2. Validate against schema (fail CI on invalid files)
3. Regenerate **generated sections** of `IMPLEMENTATION_TRACKER.md`:
   - Summary table
   - Active / upcoming work (derived from phase status + partial acceptance rules)
   - Phase logs (group events by `adr_id`, newest first)
   - Open decisions queue (union of `open_decisions` from events, deduped)
4. Preserve a short **manual preamble** in the tracker (role of documents, status value definitions) outside generated markers

Generated blocks are wrapped in HTML comments or explicit markers:

```markdown
<!-- BEGIN GENERATED:summary -->
| ADR | Title | … |
<!-- END GENERATED:summary -->
```

Hand-editing generated sections is forbidden; agents and humans edit YAML only.

### Agent pipeline changes

Update `adr-tracker` (and downstream orchestrator docs) to:

1. Write or update `docs/adr/tracker/events/{…}.yaml` on each Tracker append
2. Upsert `docs/adr/tracker/phases/{adr_id}-{phase_key}.yaml` with latest status and scope
3. Run `scripts/render_adr_tracker.py` (or instruct human to run before commit)
4. Apply CHANGELOG rules unchanged — still edit `CHANGELOG.md` directly when `verified` + user-facing

### In scope

- YAML schema, directory layout, naming conventions
- Render script and validation (unit tests with fixture YAML)
- One-time migration of existing `IMPLEMENTATION_TRACKER.md` content into YAML
- Agent definition updates for YAML writes
- CI job: validate YAML + render diff check (generated markdown matches committed output)

### Out of scope

- Moving ADR decision bodies (`NNNN-*.md`) into YAML or a database
- SQLite or other runtime database for tracker data
- MCP tools exposing tracker state to external clients
- Replacing `docs/adr/README.md` index with generated output (may link summary in a follow-up)
- Automatic CHANGELOG generation beyond existing adr-tracker rules

### Default behavior and configuration

- **Default:** Status quo until Phase 1 merges — agents continue editing markdown tracker
- **After migration:** YAML is source of truth; markdown tracker is generated artifact
- **Configuration surface:** none (dev/maintainer tooling only)

### Phased delivery

1. **Phase 1 — Schema, layout, render script**
   - Add `docs/adr/tracker/` tree, `schema.yaml`, `scripts/render_adr_tracker.py`
   - Unit tests; CI validates YAML and render output
   - No agent changes yet; manual proof with 1–2 sample phases

2. **Phase 2 — Historical migration**
   - Convert existing summary rows and phase logs to YAML event + phase files
   - Regenerate tracker; diff review; retire duplicated hand-maintained table rows
   - Document migration in `docs/adr/README.md`

3. **Phase 3 — Agent pipeline cutover**
   - Update `adr-tracker.md` and orchestrator step docs to write YAML + invoke render
   - Mark legacy markdown-append workflow deprecated

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **YAML phases + events + render script (chosen)** | Structured, git-diffable, no runtime DB; queries via script | Two artifacts (YAML + generated md); agent rewrite |
| **Status quo — single markdown tracker** | Zero migration; agents already work | Merge conflicts; no queries; fragile edits at scale |
| **SQLite tracker database** | Rich queries; atomic updates | Binary diffs; new ops burden; overkill at current scale |
| **Append-only JSONL event log** | Simple append semantics | Weaker per-phase snapshot; harder to read in review |
| **Front matter in ADR files** | Co-located with decisions | Blurs decision vs execution; ADR bodies must stay stable |
| **Generate README index from YAML** | Single status source | README is hand-curated narrative; defer |

## Consequences

### Positive

- Pipeline events become small, reviewable YAML diffs instead of table surgery
- Phase state is explicit and validatable (enum status, required fields)
- Enables maintainer scripts: `list test_debt`, `phases in_progress`, audit status progression
- Keeps ADR decisions in markdown where they belong
- Reversible: can export back to markdown or migrate to SQLite later if needed

### Negative / trade-offs

- Two-step workflow: edit YAML, run render (agents must invoke script or CI catches drift)
- Migration Phase 2 is tedious one-time work (~18 ADRs, many phase log entries)
- Generated markdown is less pleasant to read in raw form near HTML comment markers
- Phase file + event file duplication must stay in sync (render script upserts phase from latest event)

### Neutral / follow-ups

- Optional: pre-commit hook running render + validate
- Optional Phase 4: partial README index generation for ADR status column only
- Optional: `adr audit` CLI wrapping validation rules from `adr-tracker` audit mode

### Downstream work

- [`.cursor/agents/adr-tracker.md`](../../.cursor/agents/adr-tracker.md) — YAML write path
- [`.cursor/agents/adr-orchestrator.md`](../../.cursor/agents/adr-orchestrator.md) — render step in pipeline
- [`IMPLEMENTATION_TRACKER.md`](IMPLEMENTATION_TRACKER.md) — generated sections

## Implementation notes

### New artifacts

- `docs/adr/tracker/schema.yaml`
- `docs/adr/tracker/phases/*.yaml`
- `docs/adr/tracker/events/*.yaml`
- `scripts/render_adr_tracker.py`
- `scripts/migrate_tracker_to_yaml.py` (one-time; delete or archive after Phase 2)
- `mcp_server/tests/test_adr_tracker_render.py` or `scripts/tests/test_render_adr_tracker.py`

### Modified artifacts

- `docs/adr/IMPLEMENTATION_TRACKER.md` — preamble manual; body generated
- `docs/adr/README.md` — link to tracker layout
- `.cursor/agents/adr-tracker.md` — write YAML, run render
- `.github/workflows/` — CI validate + render check (if applicable)

### Dependencies

- **Runtime:** none (MCP server unchanged)
- **Dev:** PyYAML (likely already present; otherwise add to dev extras only)

### Rollout

- Opt-in through Phase 1–2; cutover in Phase 3
- No operator or deployment changes

### Data migration

- **Yes** — Phase 2 converts existing markdown phase logs and summary rows to YAML
- One PR for migration + generated tracker; no re-index or runtime data impact

## Validation

### Automated tests

- **Unit** — render script: fixture YAML → expected summary rows and phase log ordering
- **Unit** — schema validation rejects missing required fields and invalid `tracker_status`
- **Integration** — none (no external services)

### CI adoption

- Non-blocking in Phase 1; blocking render-diff check after Phase 2 migration merges

### Success criteria

1. All historical tracker rows and phase logs have equivalent YAML event/phase files
2. `adr-tracker` appends only YAML; regenerated markdown matches committed output in CI
3. No ADR decision body (`NNNN-*.md`) is modified by the tracker pipeline
4. Maintainer can list all `test_debt` entries across ADRs with a one-liner script query
