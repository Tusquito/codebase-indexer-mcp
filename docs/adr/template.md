# NNNN. Title (imperative, short)

- **Status:** Proposed | Accepted | Deprecated | Superseded
- **Date:** YYYY-MM-DD
- **Deciders:** *(names or roles, e.g. maintainers, team)*
- **Related:** *(optional — external docs, tutorials, prior decision files)*
- **Supersedes:** *(optional — link to prior decision, e.g. [0003-old-decision.md](0003-old-decision.md))*
- **Superseded by:** *(optional — link when this record is replaced)*

## Context

What problem or force are we responding to?

- *Current situation and measurable gap* — what exists today and what is missing (latency, quality, operability, …)
- *Hard constraints* — hosting, dependencies, reversibility, latency budget, team capacity
- *Requirements and goals* — what “good” looks like after the change
- *Why now* — trigger, risk, or dependency that makes deferral costly

### Evaluation stack *(optional — use when quality is multi-layer)*

When the decision touches measurable behavior, state which layers are **in scope** vs **out of scope**:

| Layer | In scope? | Notes |
|-------|-----------|-------|
| *(e.g. infrastructure correctness, component recall, ranked relevance, end-user outcome)* | yes / no / partial | |

## Decision

We will …

State the choice clearly in one or two sentences, then add supporting detail.

### In scope

- …

### Out of scope

- …

### Default behavior and configuration

- *Default:* unchanged | opt-in | breaking — **pre-release:** prefer the target state from the Decision; no backward-compat requirement unless explicitly stated
- *Configuration surface:* env vars, flags, compose profiles — or “none”

### Phased delivery *(optional)*

1. Phase 1 — …
2. Phase 2 — …

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Chosen option** | | |
| Status quo | | |
| Alternative A | | |
| Alternative B | | |

## Consequences

### Positive

- …

### Negative / trade-offs

- …

### Neutral / follow-ups

- *Maintainer tooling, multi-config compare, cross-repo fixtures, docs updates*
- …

### Downstream work

- *(Link related decision files or docs this unlocks — by filename only)*

## Implementation notes

*(Optional)* Pointers to modules, env vars, deployment files, or migration steps.

### New artifacts

- …

### Modified artifacts

- …

### Dependencies

- *Runtime:* …
- *Optional dev / benchmark extras:* …

### Maintainer tooling *(optional)*

- Helper scripts for drafting fixture labels, validating configs, or comparing variants

### Rollout

- default unchanged / opt-in / breaking

### Data migration

- re-index, relabel fixtures, baseline refresh: yes / no — describe if yes

## Validation

*(Optional)* How we know the decision worked. Delete subsections that do not apply.

### Automated tests

- *Unit* — mocked, runs in default CI
- *Integration* — requires external service; marked or skipped in CI when unavailable
- *Fixture smoke* — structural sanity (e.g. labels exist, command exits 0); not absolute latency or quality thresholds on shared runners

### Fixture-based evaluation *(when decision affects ranking, retrieval, or labeled behavior)*

- *Fixture location and format* — e.g. `path/to/fixtures/*.jsonl`; sketch schema (ids, labels, tags)
- *Label keys and alias rules* — stable id scheme; how aliases resolve to stored ids
- *Pre-flight* — validate fixtures against live store before scoring
- *Metrics* — pick 2–4 (e.g. recall@k, MRR, NDCG@k, p95 latency)
- *Baseline artifact* — committed JSON path + version field; refresh when fixtures or config change intentionally
- *Variant comparison* — A/B configs; record primary baseline and optional sidecar for alternates

### CI adoption

- *Default:* non-blocking job; upload artifacts for observation
- *Compare + threshold:* release branches only, after baseline stabilizes
- *Do not* gate every PR on curated fixtures until labels and queries are stable

### Success criteria

1. …
2. …
3. …

## Measured outcomes

*(Optional — fill after the first representative baseline on real data. Date this section. Delete entirely if the decision is not measurable.)*

### Baseline summary

| Variant | Metric A | Metric B | Notes |
|---------|----------|----------|-------|
| Primary (committed baseline) | | | |
| Alternate A/B | | | |

### Slice breakdown

| Dimension (tag, category, tenant, …) | Primary | Alternate | Takeaway |
|--------------------------------------|---------|-----------|----------|
| | | | |

### Iteration notes

- **v1 …** — what was measured; why results were misleading or incomplete
- **v2 …** — what changed (fixtures, queries, labels, config)

### Maintainer checklist

1. **Id prefix / namespace** — fixture ids must match stored artifact paths or namespaces
2. **Duplicate spans** — one logical entity may map to many chunks; label the span retrieval actually returns
3. **Field line vs container start** — labels anchor to container boundaries, not inner field or member lines
4. **Query / input wording** — prose pulls documentation; identifier-shaped inputs pull implementation
5. **Validate before score** — structural check that labels exist before running metrics

### Operational notes

- Host vs container environment differences (URLs, credentials, paths)
- When to refresh baseline vs when to relabel fixtures
- CI gating maturity: observation → compare → enforce

### Tooling added

- Helper scripts for drafting fixture labels from live runs
- Report fields for slice or category metrics

### Downstream work

- …
