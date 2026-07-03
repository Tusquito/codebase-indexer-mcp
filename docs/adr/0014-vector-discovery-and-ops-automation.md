# 0014. Adopt Qdrant vector discovery APIs and optional n8n ops hooks

- **Status:** Accepted (phase 1; phase 2 — outlier / diversity helper)
- **Date:** 2026-07-02
- **Deciders:** Maintainers
- **Related:** [Qdrant n8n Workflow Automation](https://qdrant.tech/documentation/tutorials-build-essentials/qdrant-n8n/), [Qdrant n8n platform docs](https://qdrant.tech/documentation/platforms/n8n/), [ADR 0012](0012-retrieval-only-rag-split.md)

## Context

[Qdrant’s n8n tutorial](https://qdrant.tech/documentation/tutorials-build-essentials/qdrant-n8n/) argues that vector search is **not limited to RAG memory**. It demonstrates:

1. **Recommendation API** — discovery from positive/negative example vectors (“like this, not that”)
2. **Payload indexes** — fast filtered vector search on metadata fields
3. **Distance matrix / kNN** — cluster analysis, anomaly borders, classification
4. **n8n automation** — operational workflows (monitoring, alerts, batch jobs) around Qdrant via the official Qdrant node

The codebase-indexer MCP server today focuses on **similarity search for code navigation** (`search_codebase`, hybrid RRF). Several tutorial patterns already partially apply:

| Tutorial pattern | Current state |
|------------------|---------------|
| Payload indexes on metadata | `PAYLOAD_INDEXES=true` → keyword indexes on `rel_path`, `chunk_id`, `symbol_name`, `language` in `storage/qdrant.py` |
| Filtered hybrid search | Payload filters in search tools |
| Recommendation / dissimilarity | **Phase 1 shipped** — `recommend_code` ([ADR 0014](0014-vector-discovery-and-ops-automation.md)); Track A P2 outlier helper deferred |
| Low-code ops automation | **Cron sidecar only** (`cron/reindex.py`); no n8n compose |

Code intelligence use cases that similarity-only search handles poorly:

- “Find implementations **like** this chunk but **excluding** tests and generated code” (positive + negative examples)
- “What files are **semantically distant** from the rest of this module?” (refactor / dead-code triage)
- “Alert when index health degrades” or “trigger re-index on webhook” (ops automation beyond daily cron)

We need a decision on which n8n-tutorial capabilities belong in the MCP product vs external ops tooling.

> **As of Phase 1 merge (2026-07-03):** `recommend_code` is shipped (`tools/recommend.py`, `QdrantStorage.recommend`). Track A P2 (outlier helper) and Track B (n8n compose) remain deferred.

## Decision

We will extend the platform in **two optional tracks**, inspired by the tutorial but adapted to **code collections**:

### Track A — Vector discovery MCP tools (in-server, Phase 1–2)

Add Qdrant **Recommendation API** and selective **dissimilarity / context discovery** wrappers as new MCP tools. Keep hybrid search as the default navigation path.

**Phase 1 — Recommendation search tool**

- New tool: `recommend_code`
- Inputs: `collection`, `positive_chunk_ids` or `positive_query`, optional `negative_chunk_ids` / `negative_query`, `limit`, payload filters (`language`, path glob)
- Implementation: embed positive/negative texts via existing `Embedder`; call `QdrantClient.recommend` (or `query_points` with `RecommendQuery`) on dense vector; optionally fuse with sparse channel in a follow-up
- Use cases: “similar utility, not in `*_test.go`”, “patterns like this handler, exclude legacy folder”

**Phase 2 — Dissimilarity / diversity helper (optional)**

- New tool: `find_outlier_chunks` or extend recommendation with `strategy=diverse` / score inversion
- Scope: single-collection analysis for maintainers; cap `limit` and require explicit `collection` to avoid abuse
- **Not** full image-style anomaly cluster training from the tutorial—code chunks lack clean “cluster medoids” without extra UX

### Track B — Optional n8n ops integration (compose override, Phase 3)

Document and ship an **optional** `docker-compose.n8n.yml` that:

- Adds self-hosted n8n (or references n8n AI Starter Kit patterns) on the same Docker network as MCP/Qdrant
- Provides **example workflows** (not mandatory product features):
  - Webhook → MCP `index_codebase` for CI “index on merge”
  - Scheduled health check → MCP `/health` + Qdrant cluster status → Slack/email alert
  - Post-index → call external systems (Linear, Datadog) via n8n nodes

MCP **does not** embed n8n or replace `cron/reindex.py` by default. n8n is for teams wanting visual ops automation beyond git-pull cron.

### Explicitly out of scope

- Multimodal image embedding pipelines from the tutorial (Voyage AI crop images)
- Replacing MCP search tools with n8n as the primary query path
- Running anomaly-detection cluster calibration inside the MCP index pipeline
- Mandatory n8n dependency in default compose

### Cross-track invariants

1. Discovery tools use the **same embedder and collections** as `search_codebase` (no second embedding model)
2. Recommendation results cite `chunk_id` / `rel_path` like existing search tools
3. Default deployment unchanged; discovery tools and n8n compose are opt-in
4. Payload indexes remain enabled by default (`PAYLOAD_INDEXES` in config)

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Recommendation tools + optional n8n compose (chosen)** | Maps tutorial’s “beyond RAG” value to code; ops flexibility without forking cron | New tool API surface; n8n maintenance burden |
| **Similarity search only (status quo)** | Simplest | Poor “like X not Y” discovery; ops limited to cron |
| **Full n8n-first architecture** | Rich automation | Shifts core indexing triggers out of MCP; harder self-host story |
| **Port tutorial anomaly/KNN pipelines to code** | Novel “index drift” feature | High false-positive rate on code embeddings; heavy calibration UX |

## Consequences

### Positive

- Payload index investment (already shipped) aligns with tutorial’s filtered-search guidance
- Recommendation API unlocks agent workflows that pure `search_codebase` cannot express
- Optional n8n enables CI-triggered re-index and alerting without Python cron changes
- Complements retrieval-only RAG ([ADR 0012](0012-retrieval-only-rag-split.md)) and external agents ([ADR 0013](0013-external-agent-knowledge-base.md))

### Negative / trade-offs

- Recommendation API adds embed + Qdrant call latency; needs rate limits on negative example lists
- Sparse channel fusion for recommendations is non-trivial; Phase 1 may be dense-only
- n8n compose increases support surface (credentials, upgrades) for operators who enable it
- Outlier detection on code semantics is heuristic; docs must set expectations

### Neutral / follow-ups

- Evaluate Qdrant **query points groups** for “one result per file” in recommendation responses
- Distance matrix API for maintainer dashboards—defer until GraphRAG or service-map UI needs it
- Link optional n8n workflows in `docs/DEPLOYMENT.md`, not README quickstart

## Implementation notes

### Affected paths

- Phase 1–2: `mcp_server/src/codebase_indexer/tools/recommend.py` (new), `storage/qdrant.py` (`recommend` helper), `main.py` registration, tests
- Phase 3: `docker-compose.n8n.yml`, `docs/DEPLOYMENT.md`, example workflow JSON under `docs/examples/n8n/`

### Configuration

| Variable | Phase | Default | Purpose |
|----------|-------|---------|---------|
| `RECOMMEND_ENABLED` | 1 | `true` | Master switch for recommendation tool registration |
| `RECOMMEND_MAX_EXAMPLES` | 1 | `10` | Cap positive + negative example count per request |

### Rollout

- Phase 1: new MCP tool; default embed backend unchanged
- Phase 3: opt-in compose file only

### Re-index

**No** for enabling tools. **Yes** if recommendation expects payload fields not yet indexed (none anticipated for Phase 1).

## Validation

| Phase | Checks |
|-------|--------|
| 1 | Unit test: mocked Qdrant `recommend` with positive/negative vectors; integration test on fixture repo excludes negative paths |
| 2 | Outlier tool returns chunks below similarity threshold with bounded `limit` |
| 3 | Compose smoke: n8n workflow triggers MCP `index_codebase` via HTTP tool call |

Success criteria:

- `recommend_code` returns chunks similar to positive examples and suppresses negative-example neighborhoods on fixture data
- With n8n compose disabled, zero change to index duration and existing search tools
- Payload indexes remain idempotent at collection creation (existing `PAYLOAD_INDEXES` behavior in `config.py`)
