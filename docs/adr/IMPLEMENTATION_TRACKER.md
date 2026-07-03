# ADR implementation tracker

Living record of **what we chose to build**, **implementation progress**, and **runtime choices** while executing ADRs.

| Document | Role |
|----------|------|
| `NNNN-*.md` | **Decision** — context, alternatives, consequences (edit only on formal Accept / Supersede) |
| [`README.md`](README.md) index | **ADR status** — Proposed / Accepted / Superseded (index row only) |
| **This file** | **Execution** — phases, choices, deviations, verification, links |
| [`CHANGELOG.md`](../../CHANGELOG.md) | **Shipped** — user-facing release notes when behavior changes |

Do **not** use ADR bodies as a task list or implementation journal. Append pipeline outcomes here instead.

## Tracker status values

| Status | Meaning |
|--------|---------|
| `not_started` | No implementation work scheduled |
| `candidate` | Under consideration (e.g. prioritizer recommendation) |
| `planned` | Implementation plan exists for a phase |
| `in_progress` | Developer actively implementing a phase |
| `implemented` | Code landed; smoke checks passed; awaiting full tests |
| `verified` | Test agent confirmed; ready for git / release |
| `merged` | PR merged (link below) |
| `deferred` | Explicitly postponed |

**Delivery unit:** one ADR **phase** = one pull request (see agent pipeline).

## Summary

| ADR | Title | ADR status | Phase | Tracker | Chosen scope | Last updated |
|-----|-------|------------|-------|---------|--------------|--------------|
| [0002](0002-graphrag-neo4j-qdrant.md) | Optional GraphRAG (Neo4j + Qdrant) | Proposed | — | `not_started` | — | — |
| [0003](0003-hybrid-search-rrf-default.md) | Hybrid search RRF default | Accepted | all | `merged` | Shipped | 2026-07-02 |
| [0004](0004-collection-per-project-isolation.md) | Collection-per-project isolation | Accepted | all | `merged` | Shipped | 2026-07-02 |
| [0005](0005-mcp-retrieval-connector.md) | MCP retrieval connector | Accepted | all | `merged` | Shipped | 2026-07-02 |
| [0006](0006-explicit-fastembed-pipeline.md) | Explicit FastEmbed pipeline | Accepted | all | `merged` | Shipped | 2026-07-02 |
| [0007](0007-ranx-retrieval-evaluation.md) | Golden-set eval (ranx) | Accepted | all | `merged` | `eval_retrieval.py` + fixtures | 2026-07-02 |
| [0008](0008-optional-colbert-reranking.md) | Optional ColBERT reranking | Proposed | 1 | `verified` | Config (`RERANK_ENABLED=false` default, `COLBERT_EMBED_MODEL`, `RERANK_PREFETCH`, `RERANK_MAX_QUERY_TOKENS`); `ColbertOnnxBackend` via fastembed; multivector `colbert` + MAX_SIM rerank in `qdrant.py`; per-collection hybrid prefetch + ColBERT rerank then `fuse_cross_collection_rrf`; pipeline third embed pass (sequential); synthetic CI integration test + `@pytest.mark.slow` + `RUN_SLOW_COLBERT=1`; operator re-index docs; defer adaptive rerank, per-tool overrides, cross_reference/service_map wiring | 2026-07-03 |
| [0009](0009-multi-hop-retrieval-strategies.md) | Multi-hop retrieval | Accepted (phase 1) | 1 | `merged` | Client decomposition docs + golden tags | 2026-07-02 |
| [0009](0009-multi-hop-retrieval-strategies.md) | Multi-hop retrieval | Accepted (phase 1) | 2+ | `not_started` | Server-side hop fusion TBD | — |
| [0010](0010-defer-ragas-to-client.md) | Defer Ragas to client | Accepted | all | `merged` | Export script + DEPLOYMENT guide | 2026-07-02 |
| [0011](0011-ollama-only-dense-embedding.md) | Ollama-only dense embedding | Accepted | all | `merged` | See CHANGELOG [Unreleased] | 2026-07-02 |
| [0012](0012-retrieval-only-rag-split.md) | Retrieval-only RAG split | Accepted | all | `merged` | Shipped | 2026-07-02 |
| [0013](0013-external-agent-knowledge-base.md) | External agent knowledge base | Accepted | all | `merged` | MCP tools surface | 2026-07-02 |
| [0014](0014-vector-discovery-and-ops-automation.md) | Vector discovery + n8n ops | Proposed | — | `not_started` | — | — |

Superseded [0001](0001-pluggable-embed-backends.md) — historical; implementation superseded by [0011](0011-ollama-only-dense-embedding.md).

## Active and upcoming work

### Proposed ADRs (not started)

| ADR | Notes |
|-----|-------|
| 0002 | Four phases; default deploy stays Qdrant-only |
| 0008 | Phase 1 `verified`; pending merge; depends on [0003](0003-hybrid-search-rrf-default.md); eval via [0007](0007-ranx-retrieval-evaluation.md) |
| 0014 | Track A (MCP tools) vs Track B (n8n compose) |

### Partial acceptance

| ADR | Done | Remaining |
|-----|------|-----------|
| 0009 | Phase 1 — `SEARCH_BEHAVIOR.md` multi-hop section, golden `multi_hop` tags | Phase 2+ server mechanisms; optional graph-backed hops per [0002](0002-graphrag-neo4j-qdrant.md) |

---

## Phase logs

Append newest entries at the **top** of each ADR section. Copy summaries from each pipeline step's Tracker append output.

### Template (copy per entry)

```markdown
#### YYYY-MM-DD — <event> (<step or human>)
- **Phase / PR:** …
- **Choices:** …
- **Deviations:** none | …
- **Code evidence:** `path` or grep result
- **Test debt:** … (from implementation step)
- **Verify:** … (from test verification step)
- **Git:** PR #… (after merge)
- **Changelog:** yes / no — link section if yes
```

---

### ADR 0008 — Optional ColBERT reranking

#### 2026-07-03 — verification
- **Phase / PR:** Phase 1 — optional ColBERT multivector reranking
- **Tracker status:** `verified`
- **Choices:** `COLBERT_EMBED_MODEL=colbert-ir/colbertv2.0` (128-d tokens); `HnswConfigDiff(m=0)` on colbert vector; cross-collection rerank per-collection then global RRF; CI uses synthetic multivectors; real model via `@pytest.mark.slow`; ColBERT index embed sequential after dense+sparse
- **Deviations:** none
- **Code evidence:** `config.py`, `colbert_onnx.py`, `embedder.py`, `qdrant.py`, `search_common.py`, `test_storage_integration.py`, `bench.py`, `eval_retrieval.py`
- **Test debt:** ranx eval tests skip without `--extra benchmark`; golden-set MRR delta manual via `eval_retrieval --rerank`; no unit test for colbert mismatch recreate; slow ColBERT smoke opt-in only
- **Verify:** tests run + plan compliance pass (217 passed); review rounds: 1
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Phase 1 — optional ColBERT multivector reranking (index-time multivectors + query-time MAX_SIM rerank over hybrid prefetch pool)
- **Tracker status:** `implemented`
- **Choices:** `COLBERT_EMBED_MODEL` default `colbert-ir/colbertv2.0` (128-d per token); `HnswConfigDiff(m=0)` on `colbert` vector; per-collection hybrid prefetch + ColBERT MAX_SIM rerank then global `fuse_cross_collection_rrf`; ColBERT always sequential after dense+sparse at index time; synthetic multivectors in CI integration test; real model behind `@pytest.mark.slow` + `RUN_SLOW_COLBERT=1`; `RERANK_ENABLED=false` default preserves existing behavior
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/indexer/backends/colbert_onnx.py`, `mcp_server/src/codebase_indexer/indexer/embedder.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/src/codebase_indexer/tools/search_common.py`, `mcp_server/tests/test_storage_integration.py`, `docs/SEARCH_BEHAVIOR.md`, `.env.example`
- **Test debt:** cross-collection rerank integration test; golden-set MRR with `--rerank`; rerank mismatch recreate test; wire ColBERT into cross_reference/service_map search when rerank enabled
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — plan
- **Phase / PR:** Phase 1 — optional ColBERT multivector reranking (index-time multivectors + query-time MAX_SIM rerank over hybrid prefetch pool)
- **Tracker status:** `planned`
- **Choices:** **`COLBERT_EMBED_MODEL` default:** `colbert-ir/colbertv2.0` (128-d per token). **ADR `m=768` prose:** treat as documentation error for HNSW knob; implement `HnswConfigDiff(m=0)` on `colbert` vector; per-token `size` from registry (128 for default model). **Cross-collection rerank:** per-collection hybrid prefetch + ColBERT MAX_SIM rerank, then existing global `fuse_cross_collection_rrf`. **CI ColBERT testing:** integration test uses synthetic multivectors only; real model test `@pytest.mark.slow`. **Index-time memory:** ColBERT always sequential after dense+sparse when rerank enabled. **ADR Accept:** formal Proposed → Accepted + README index update is pre-merge follow-up. **Operator messaging:** re-index required when enabling rerank — in `.env.example` + `SEARCH_BEHAVIOR.md`. **Chosen scope:** Config (`RERANK_ENABLED`, `COLBERT_EMBED_MODEL`, `RERANK_PREFETCH`, `RERANK_MAX_QUERY_TOKENS`); `ColbertOnnxBackend` via fastembed `LateInteractionTextEmbedding`; multivector `colbert` schema + MAX_SIM rerank query in `qdrant.py`; pipeline third embed pass (sequential after dense+sparse); `search_common` wiring; synthetic integration test + optional `@pytest.mark.slow` real-model test; `eval_retrieval.py` / `bench.py` rerank deltas; operator re-index docs in `.env.example` + `SEARCH_BEHAVIOR.md`; defer adaptive rerank and per-tool overrides.
- **Assumptions:** Qdrant v1.18.2 supports multivector + prefetch rerank; fastembed supports default model without new deps
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** synthetic multivector integration test required for CI; real-model coverage optional via `@pytest.mark.slow`
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — prioritization
- **Phase / PR:** Phase 1 — optional ColBERT multivector reranking (index-time multivectors + query-time MAX_SIM rerank over hybrid prefetch pool)
- **Tracker status:** `candidate`
- **Choices:** Prioritize search-quality increment on existing Qdrant stack over greenfield Neo4j (0002) or recommendation API (0014); deliver single phase per pipeline rule; require formal Accept of Proposed ADR 0008 before dev. **Chosen scope:** config (`RERANK_ENABLED`, `COLBERT_EMBED_MODEL`, `RERANK_PREFETCH`), ColBERT fastembed backend, multivector schema + rerank query in `qdrant.py`, pipeline third embed pass, integration test, `eval_retrieval.py` quality delta, P95 in `bench.py`; defer adaptive rerank and per-tool overrides. **Why now:** Accepted ADR 0003 explicitly deferred ColBERT reranking; hybrid RRF (0003), eval harness (0007), and Ollama-only dense (0011) are merged; no rerank code exists; opt-in flag preserves default deployment; measurable via golden set MRR/NDCG and `bench.py` latency. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** —
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing unknown (likely yes when flag enabled)

---

### ADR 0002 — GraphRAG (Neo4j + Qdrant)

*No implementation log yet.*

---

### ADR 0009 — Multi-hop retrieval

#### 2026-07-02 — Phase 1 delivered
- **Phase / PR:** Phase 1 (docs + golden-set tags)
- **Choices:** Client-orchestrated decomposition; no new server code in phase 1
- **Code evidence:** `docs/SEARCH_BEHAVIOR.md`, `benchmarks/fixtures/golden_queries.jsonl` multi_hop tags
- **Changelog:** no (documentation-only phase)

---

### ADR 0014 — Vector discovery and ops automation

*No implementation log yet.*

---

## How to update

Pipeline steps output a **Tracker append** block; the **invoker** (or a dedicated tracker specialist) applies file edits. ADR pipeline steps do not edit tracker or changelog files directly.

| Step | Role | Tracker status | Changelog |
|------|------|----------------|-----------|
| 1 | Prioritization | `candidate` | no |
| 2 | Planning | `planned` | no (record **user-facing: yes/no**) |
| 3 | Implementation | `implemented` | no |
| 3a | Code review | — (loop) | no |
| 3b | Bug fix | — (loop) | no |
| 4 | Verification (review clean) | `verified` | yes **only if** user-facing |
| 5 | Git operator (prepare) | — | no |
| 5a | PR review | — | no |
| 5b | Git operator (record merge) | `merged` + PR link | no |
| 6 | Release | optional | move `[Unreleased]` → versioned section |

1. **Prioritization** — append log; summary row → `candidate`.
2. **Planning** — append log; summary row → `planned`; set chosen scope + user-facing flag.
3. **Implementation** — append log; summary row → `implemented`; code evidence + test debt.
3a–3b. **Review / fix loop** — invoker passes `## Review findings` (`Verdict: needs_fix`) to bug fix; passes `## ADR bug fix report` back to code review. Repeat until `Verdict: clean`. No tracker append during the loop.
4. **Verification** — when review is clean, apply Tracker append (`verified`); if user-facing, add CHANGELOG `[Unreleased]` bullet when applying the append.
5. **Git prepare** — feature branch `adr/NNNN-phase-N-<slug>`, grouped conventional commits, push, **PR into `main`**. No tracker append.
5a. **PR review** — validate PR diff and description against plan; `approve` before merge. No tracker append.
5b. **Record merge** — when PR merged, apply Tracker append (`merged`) with PR link.
6. **Release** — version CHANGELOG; do not duplicate tracker logs in changelog prose.

Apply steps 1–5 by passing each step's **Tracker append** output to the tracker update process (invoker or orchestrator).

## Open decisions queue

Decisions made during implementation that are **not** worth amending the ADR file — record here until promoted to a new ADR or the index status changes.

| Date | ADR | Question | Decision | Promote to ADR? |
|------|-----|----------|----------|-----------------|
| 2026-07-03 | 0008 | Accept ADR 0008 (Proposed → Accepted)? | Pre-merge follow-up: formal Accept + README index update before dev | no |
| 2026-07-03 | 0008 | Select `COLBERT_EMBED_MODEL` | `colbert-ir/colbertv2.0` (128-d per token) | no |
| 2026-07-03 | 0008 | Confirm operator re-index messaging for `RERANK_ENABLED=true` | Document in `.env.example` + `SEARCH_BEHAVIOR.md` | no |
| 2026-07-03 | 0008 | ADR `m=768` HNSW knob on `colbert` vector | Treat ADR prose as documentation error; `HnswConfigDiff(m=0)`; per-token `size` from registry | no |
| 2026-07-03 | 0008 | Cross-collection rerank ordering | Per-collection hybrid prefetch + ColBERT MAX_SIM rerank, then `fuse_cross_collection_rrf` | no |
| 2026-07-03 | 0008 | CI ColBERT test strategy | Synthetic multivectors in integration test; real model `@pytest.mark.slow` only | no |
| 2026-07-03 | 0008 | Index-time ColBERT embed ordering | Always sequential after dense+sparse when rerank enabled | no |
| 2026-07-03 | 0008 | Default rerank behavior | `RERANK_ENABLED=false` preserves existing hybrid RRF-only search | no |
| 2026-07-03 | 0008 | Slow real-model ColBERT test gate | `@pytest.mark.slow` + `RUN_SLOW_COLBERT=1` | no |
