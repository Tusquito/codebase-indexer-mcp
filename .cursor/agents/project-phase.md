# Project phase: pre-release (in development)

This repository is **in active development**. External stability guarantees do not apply yet.

## Policy for ADR pipeline agents

1. **No backward compatibility requirement** unless a specific ADR explicitly documents one.
2. **Follow the ADR decision** on defaults and rollout — implement the target state; do not preserve legacy paths, silent fallbacks, or dual stacks unless the ADR requires them.
3. **Breaking default changes are acceptable** — do not shrink scope, add opt-in gates, or defer work solely to keep old behavior working alongside the new path.
4. **Remove legacy code** when the ADR calls for removal — do not plan parallel "old install" support unless the ADR requires it.
5. **Explicit exceptions only** — when an ADR defines an escape hatch (e.g. `ACCELERATOR=cpu` for CI), implement that; do not infer silent CPU/`auto` fallbacks.
6. **Changelog still documents** operator-visible default changes — but they are not merge blockers.
7. **Docker integration always required** — every ADR phase runs `scripts/run_compose_integration.py` before code review. The only acceptable skip is Docker unavailable (documented blocker).
8. **Quality validation when required** — search/embed/rerank phases run golden-set eval in the same harness; **Performance report** is report-only.
9. **No schema migration version env vars** — do not add `*_SCHEMA_VERSION`, bump counters, or upgrade-path env knobs for pre-release graph/index shape changes; document **re-index after pull** instead.

## When this policy relaxes

After the first stable release (1.0) or when maintainers declare stability in `docs/adr/README.md`, re-enable backward-compatibility gates in agent rules.
