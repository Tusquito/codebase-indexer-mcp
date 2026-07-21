# 0031. Split MCP liveness from dependency readiness

- **Status:** Proposed
- **Date:** 2026-07-21
- **Deciders:** Maintainers
- **Related:** [0018](0018-telemetry-observability-otel-prometheus.md) (metrics scrape), [0015](0015-colbert-http-sidecar.md) (ColBERT sidecar), [0025](0025-huggingface-tei-dense-embedding.md) (TEI dense), [0002](0002-graphrag-neo4j-qdrant.md) (Neo4j when graph on), [DEPLOYMENT.md](../DEPLOYMENT.md)

## Context

Compose marks `codeindexer_mcp` healthy via `GET /health`, which today always returns `{"status":"ok"}` once the HTTP server is up. Startup may log `model_preload_failed_continuing` and keep serving while TEI, remote ColBERT, or (when enabled) Neo4j is unavailable. `codeindexer_cron` and operators then treat the stack as ready and hit opaque embed/tool failures.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Infrastructure correctness | yes | Dependency probes for required sidecars |
| Retrieval quality | no | Unchanged ranking paths |
| End-user MCP contract | partial | New readiness route; `/health` stays liveness |

## Decision

We will **split liveness from readiness** on the MCP HTTP surface:

- Keep `GET /health` as **liveness** (process up; no dependency checks; auth-exempt).
- Add `GET /ready` (name final in Phase 1) as **readiness**: fails closed when required dense TEI is unreachable; when `RERANK_ENABLED=true` and remote ColBERT, sidecar `/health` must be OK; when `GRAPH_ENABLED=true`, Neo4j bolt connectivity must succeed.
- Point Compose `mcp_server` healthcheck (and document cron expectations) at **readiness**, not liveness.

### In scope

- Readiness handler + Settings-driven dependency set
- Compose healthcheck switch
- Unit tests (mocked backends) + Docker integration assertion
- DEPLOYMENT / `.env.example` operator notes

### Out of scope

- Kubernetes probes beyond documenting the two URLs
- Changing TEI/ColBERT/Neo4j images
- Fail-hard index jobs on Neo4j write errors (separate policy)

### Default behavior and configuration

- *Default:* readiness required for Compose health; no new env required for baseline TEI
- *Configuration surface:* reuse existing `TEI_URL`, `RERANK_ENABLED`, `COLBERT_URL`, `GRAPH_ENABLED`, `NEO4J_*`; optional `READY_CHECK_TIMEOUT` if needed

### Phased delivery

1. Phase 1 — `/ready` + compose healthcheck + unit/integration tests
2. Phase 2 — Optional stricter cron gating / startup fail-closed when `PRELOAD_MODELS=true` and required deps down

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Chosen: `/health` liveness + `/ready` readiness** | Standard probe split; cron/compose stop early | One new route |
| Status quo (soft `/health`) | Zero code | Cron races broken deps |
| Fail process on preload error | Strong fail-fast | Harder local bring-up / TEI late start |
| Only document “wait for logs” | No code | Operators miss it |

## Consequences

### Positive

- Compose/cron do not mark MCP ready until TEI (and optional ColBERT/Neo4j) answer
- Clearer ops debugging (`/health` vs `/ready`)

### Negative / trade-offs

- Longer `start_period` may be needed on first TEI model download
- Host TEI / Metal external TEI must be up before readiness passes

### Downstream work

- Wire Prometheus scrape only after ready if desired ([0018](0018-telemetry-observability-otel-prometheus.md) Phase 3)

## Implementation notes

### New artifacts

- Readiness check helper (e.g. `codebase_indexer/health.py` or under `main.py`)
- Unit tests for dependency matrix

### Modified artifacts

- `mcp_server` FastMCP/Starlette routes; `docker-compose.yml` healthcheck; `docs/DEPLOYMENT.md`; `.env.example`

### Rollout

- opt-in until Phase 1 merges, then default Compose uses `/ready`

### Data migration

- none

## Validation

### Automated tests

- *Unit* — mocked TEI/ColBERT/Neo4j success and failure paths
- *Integration* — live compose: `/ready` 200 when stack healthy; forced TEI stop → `/ready` non-200 while `/health` 200

### Success criteria

1. With full GPU stack up, `/ready` returns 200 and Compose marks `mcp_server` healthy
2. With TEI stopped, `/ready` fails and Compose does not report healthy after retries
3. `/health` remains 200 without auth whenever the process is up
