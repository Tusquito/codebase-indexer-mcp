# Project agents

ADR pipeline agents for this repository. **Project-level only** — versioned here; do not copy to `~/.cursor/agents/`.

Orchestrator and Tasks read definitions from `.cursor/agents/<name>.md` in the workspace.

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
| 5a–5b | `adr-pr-review` ↔ `adr-pr-babysit` | PR review loop (babysit: cloud) |
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

## Docs

- [`docs/adr/README.md`](../../docs/adr/README.md) — ADR index and agent policy
- [`docs/adr/IMPLEMENTATION_TRACKER.md`](../../docs/adr/IMPLEMENTATION_TRACKER.md) — pipeline steps and resume

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
