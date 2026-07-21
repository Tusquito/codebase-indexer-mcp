# Project agents

ADR pipeline agents for this repository. **Project-level only** — versioned here; do not copy to `~/.cursor/agents/`.

Orchestrator and Tasks read definitions from `.cursor/agents/<name>.md` in the workspace. Each agent is invoked via its **native subagent type** (e.g. `subagent_type: "adr-planner"`), never `generalPurpose`.

## Model policy

Every agent declares a `model:` in its frontmatter — currently uniform `cursor-grok-4.5-high-fast` for the orchestrator workflow — but that field is documentation of intent, not an active pin. The `Task` tool does not read a subagent's frontmatter automatically; omitting `model` on a Task call makes the subagent inherit the **caller's** model instead. The orchestrator must explicitly pass `model:` on every Task call, looked up per-agent — see `adr-orchestrator.md` → [Delegation](adr-orchestrator.md#delegation) for the lookup table and [Model policy](adr-orchestrator.md#model-policy-mandatory). Don't hand-edit an agent's pinned model to "be safe" for one run — use the orchestrator's `Model override:` invoker field instead.

## Project phase

**Pre-release (in development).** See [`project-phase.md`](project-phase.md). ADR planning and implementation agents must **not** treat backward compatibility, legacy dual paths, or "default unchanged" as constraints unless an ADR explicitly requires them. **Docker integration is mandatory every phase**; **quality validation** runs for search/embed/rerank phases via the same harness.

## ADR pipeline

| Step | Agent | Role |
|------|-------|------|
| — | `adr-orchestrator` | Runs full pipeline or resumes from a later step |
| 1 | `adr-prioritizer` | Pick next ADR/phase → `candidate` |
| 2 | `adr-planner` | Code-ready plan → `planned` |
| 3 | `adr-developer` | Implement phase → `implemented` |
| 3.5 | `adr-integration-tester` | Compose deploy + live integration tests |
| 3a–4 | `adr-code-reviewer` ↔ `adr-bug-fixer` | Review loop → `verified` |
| 5 | `adr-git-operator` | Branch, commits, PR → main |
| 5a–5b | `adr-pr-review` ↔ `adr-pr-babysit` | PR review loop (local) |
| 6 | `adr-finisher` | Merge, accept ADR, optional release → `merged` |
| 7 | `adr-git-operator` (`cleanup`) | Commit tracker, push main, delete branch |
| — | `adr-tracker` | Apply Tracker append + CHANGELOG rules |

## Handoff artifacts

| Section | Producer | Consumer |
|---------|----------|----------|
| `## Tracker append` | prioritizer, planner, developer, code-reviewer, finisher | orchestrator → tracker |
| `## Review findings` | code-reviewer | bug-fixer, orchestrator |
| `## ADR integration report` | integration-tester | code-reviewer, orchestrator |
| `## PR review findings` | pr-review | pr-babysit, finisher, orchestrator |

## Tracker storage (ADR 0019)

Per [ADR 0019](../../docs/adr/0019-yaml-structured-adr-tracker.md), the implementation tracker is **structured YAML** — the source of truth — and `IMPLEMENTATION_TRACKER.md` is a **generated artifact**:

- [`docs/adr/tracker/schema.yaml`](../../docs/adr/tracker/schema.yaml) — field/enum contract
- [`docs/adr/tracker/phases/`](../../docs/adr/tracker/phases/) — one snapshot per ADR phase
- [`docs/adr/tracker/events/`](../../docs/adr/tracker/events/) — append-only pipeline events
- [`scripts/render_adr_tracker.py`](../../scripts/render_adr_tracker.py) — validates YAML and regenerates the markdown (`--validate-only`, `--check`)

`adr-tracker` writes an event + upserts the phase file, then runs the render script — **never** hand-edit inside the `<!-- BEGIN/END GENERATED:* -->` markers. CI runs `render_adr_tracker.py --check` as a blocking step.

## Docs

- [`docs/adr/README.md`](../../docs/adr/README.md) — ADR index and agent policy
- [`docs/adr/IMPLEMENTATION_TRACKER.md`](../../docs/adr/IMPLEMENTATION_TRACKER.md) — pipeline steps and resume (generated from tracker YAML)

## Invoke

```
Run the ADR orchestrator.
```

```
Resume from: 6
ADR id: 0008
Phase / track: Phase 1
PR reference: #1
```
