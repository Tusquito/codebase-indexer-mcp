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
| [0008](0008-optional-colbert-reranking.md) | Optional ColBERT reranking | Accepted (phase 1 — optional ColBERT multivector reranking) | 1 | `merged` | Config (`RERANK_ENABLED=false` default, `COLBERT_EMBED_MODEL`, `RERANK_PREFETCH`, `RERANK_MAX_QUERY_TOKENS`); `ColbertOnnxBackend` via fastembed; multivector `colbert` + MAX_SIM rerank in `qdrant.py`; per-collection hybrid prefetch + ColBERT rerank then `fuse_cross_collection_rrf`; pipeline third embed pass (sequential); synthetic CI integration test + `@pytest.mark.slow` + `RUN_SLOW_COLBERT=1`; operator re-index docs; [PR #1](https://github.com/Tusquito/codebase-indexer-mcp/pull/1) | 2026-07-03 |
| [0008](0008-optional-colbert-reranking.md) | Optional ColBERT reranking | Accepted (phase 1) | 2 — track 1 (xref/service_map rerank wiring) | `merged` | Shared `dispatch_search()` in `search_common.py`; xref semantic/import via `run_search()`; service_map batched discovery via `dispatch_search()` with pre-embedded colbert vectors; tool-specific `min_score` retained (0.3 / 0.25); unit tests + `SEARCH_BEHAVIOR.md`; default deploy unchanged (`RERANK_ENABLED=false`); adaptive rerank and per-tool overrides deferred to track 2; [PR #4](https://github.com/Tusquito/codebase-indexer-mcp/pull/4) | 2026-07-03 |
| [0009](0009-multi-hop-retrieval-strategies.md) | Multi-hop retrieval | Accepted (phase 1) | 1 | `merged` | Client decomposition docs + golden tags | 2026-07-02 |
| [0009](0009-multi-hop-retrieval-strategies.md) | Multi-hop retrieval | Accepted (phase 1) | 2+ | `not_started` | Server-side hop fusion TBD | — |
| [0010](0010-defer-ragas-to-client.md) | Defer Ragas to client | Accepted | all | `merged` | Export script + DEPLOYMENT guide | 2026-07-02 |
| [0011](0011-ollama-only-dense-embedding.md) | Ollama-only dense embedding | Accepted | all | `merged` | See CHANGELOG [Unreleased] | 2026-07-02 |
| [0012](0012-retrieval-only-rag-split.md) | Retrieval-only RAG split | Accepted | all | `merged` | Shipped | 2026-07-02 |
| [0013](0013-external-agent-knowledge-base.md) | External agent knowledge base | Accepted | all | `merged` | MCP tools surface | 2026-07-02 |
| [0014](0014-vector-discovery-and-ops-automation.md) | Vector discovery + n8n ops | Proposed | — | `not_started` | — | — |
| [0015](0015-colbert-http-sidecar.md) | ColBERT HTTP sidecar | Accepted | 1 | `merged` | Opt-in `COLBERT_EMBED_BACKEND=remote` + `colbert_worker` sidecar; default in-process ONNX unchanged; FastAPI lifespan preload; `ColbertRemoteBackend` httpx client; `docker-compose.colbert-worker.yml` with shared `fastembed_cache`; `.env.example` + `SEARCH_BEHAVIOR.md`; [PR #2](https://github.com/Tusquito/codebase-indexer-mcp/pull/2) | 2026-07-03 |
| [0015](0015-colbert-http-sidecar.md) | ColBERT HTTP sidecar | Accepted | 2 | `merged` | GPU sidecar via `colbert_worker/Dockerfile.gpu` (`onnxruntime-gpu==1.26.0`, `python:3.12-slim`); compose override `docker-compose.colbert-worker.gpu.yml` (NVIDIA reservations mirroring Ollama); `COLBERT_DEVICE_IDS` → `ColbertOnnxBackend.device_ids`; worker `/health` reports `device` + `cuda_available`; fail-fast CUDA preload; `bench_colbert_sidecar.py` remote throughput bench; single-GPU 8GB OOM documented (no auto-scheduler); CI-safe mocked/skipped GPU tests + non-blocking GPU Dockerfile CI job; [PR #3](https://github.com/Tusquito/codebase-indexer-mcp/pull/3) | 2026-07-03 |
| [0015](0015-colbert-http-sidecar.md) | ColBERT HTTP sidecar | Accepted | 3+ | `not_started` | MCP slim image when remote-only | — |

Superseded [0001](0001-pluggable-embed-backends.md) — historical; implementation superseded by [0011](0011-ollama-only-dense-embedding.md).

## Active and upcoming work

### Proposed ADRs (not started)

| ADR | Notes |
|-----|-------|
| 0002 | Four phases; default deploy stays Qdrant-only |
| 0014 | Track A (MCP tools) vs Track B (n8n compose) |

### Partial acceptance

| ADR | Done | Remaining |
|-----|------|-----------|
| 0008 | Phase 1 — opt-in ColBERT multivector rerank ([PR #1](https://github.com/Tusquito/codebase-indexer-mcp/pull/1)); Phase 2 track 1 — xref/service_map rerank wiring ([PR #4](https://github.com/Tusquito/codebase-indexer-mcp/pull/4)) | Phase 2 track 2 — adaptive rerank vs per-tool override (TBD); per-tool overrides |
| 0009 | Phase 1 — `SEARCH_BEHAVIOR.md` multi-hop section, golden `multi_hop` tags | Phase 2+ server mechanisms; optional graph-backed hops per [0002](0002-graphrag-neo4j-qdrant.md) |
| 0015 | Phase 1 — HTTP sidecar + remote backend ([PR #2](https://github.com/Tusquito/codebase-indexer-mcp/pull/2)); Phase 2 — GPU worker + benchmark ([PR #3](https://github.com/Tusquito/codebase-indexer-mcp/pull/3)) | MCP slim image when remote-only (phase 3+) |

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

#### 2026-07-03 — merge
- **Phase / PR:** Phase 2 — track 1 (xref/service_map rerank wiring) — [PR #4](https://github.com/Tusquito/codebase-indexer-mcp/pull/4)
- **Tracker status:** `merged`
- **Choices:** squash merge `fcf2e18` on feature branch `adr/0008-phase-2-xref-service-map-rerank`; ADR accept skipped (unchanged — Accepted phase 1); release skipped; Phase 2 track 2 deferred (adaptive rerank vs per-tool override)
- **Deviations:** none
- **Code evidence:** merged via PR #4 (`adr/0008-phase-2-xref-service-map-rerank`)
- **Test debt:** carried from verification — import-phrased xref colbert wiring test; single-collection xref semantics regression test; optional slow integration rerank smoke for xref/service_map
- **Verify:** carried from verification — 17 targeted tests passed; 235-suite tests passed (242 with fastapi env); ruff clean; review rounds: 1
- **Git:** [PR #4](https://github.com/Tusquito/codebase-indexer-mcp/pull/4) merged (squash `fcf2e18`)
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — verification
- **Phase / PR:** Phase 2 — track 1 (xref/service_map rerank wiring)
- **Tracker status:** `verified`
- **Choices:** Shared `dispatch_search` helper in `search_common.py`; xref semantic/import via `run_search()`; service_map batched discovery via `dispatch_search()` with pre-embedded colbert vectors; tool-specific `min_score` retained (0.3 / 0.25); default deploy unchanged (`RERANK_ENABLED=false`); adaptive rerank and per-tool overrides deferred to track 2
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/tools/search_common.py`, `cross_references.py`, `service_map.py`, `mcp_server/tests/test_search_common.py`, `test_cross_references.py`, `test_service_map.py`, `docs/SEARCH_BEHAVIOR.md`
- **Test debt:** import-phrased xref colbert wiring test; single-collection xref semantics regression test; optional slow integration rerank smoke for xref/service_map
- **Verify:** tests run + plan compliance pass — 17 targeted tests passed; 235-suite tests passed (242 with fastapi env); ruff clean; review rounds: 1
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Phase 2 — track 1 (cross_reference / service_map rerank wiring)
- **Tracker status:** `implemented`
- **Choices:** Extracted shared `dispatch_search()` in `search_common.py`; xref semantic/import paths route through `run_search()`; service_map batched discovery loop routes through `dispatch_search()` with pre-embedded colbert vectors; default deploy unchanged (`RERANK_ENABLED=false`)
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/tools/search_common.py`, `mcp_server/src/codebase_indexer/tools/cross_references.py`, `mcp_server/src/codebase_indexer/tools/service_map.py`, `mcp_server/tests/test_search_common.py`, `mcp_server/tests/test_cross_references.py`, `mcp_server/tests/test_service_map.py`, `docs/SEARCH_BEHAVIOR.md`
- **Test debt:** import-phrased xref colbert wiring test; single-collection xref semantics regression; optional slow integration rerank smoke for xref/service_map
- **Verify:** —
- **Git:** pending
- **Changelog:** no

#### 2026-07-03 — plan
- **Phase / PR:** Phase 2 — track 1 (cross_reference / service_map rerank wiring)
- **Tracker status:** `planned`
- **Choices:** Shared `dispatch_search` helper (not duplicate colbert pass-through in each tool); keep tool-specific internal `min_score` (0.3 / 0.25) — ignored on hybrid/rerank via existing `qdrant.py` logic; no new config/infra; single PR; no ADR accept/index update
- **Assumptions:** Phase 1 + ADR 0015 merged; `embed_queries` batch already computes colbert when rerank on — wiring only adds Qdrant query stage; `eval_retrieval --rerank` validates `run_search` path not tool handlers directly
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** unit tests in `test_search_common.py`, `test_cross_references.py`, `test_service_map.py`; `SEARCH_BEHAVIOR.md` xref/service_map rerank note
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — prioritization
- **Phase / PR:** Phase 2 — cross_reference / service_map rerank wiring (first track of Phase 2+)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0008 Phase 2 track 1 over 0015 Phase 3 slim image, Proposed 0002 GraphRAG Phase 1, Proposed 0014 recommendation tool, and undefined 0009 Phase 2+ server fusion; single phase per pipeline rule; no new infrastructure; measurable via existing `eval_retrieval.py --rerank` and golden set. **Chosen scope:** Route semantic search paths in `cross_references.py` and `service_map.py` through the same ColBERT-aware search dispatch as `search_common.run_search` (pass `colbert_vector`; align hybrid+rerank score behavior); add integration/unit tests; defer adaptive rerank and per-tool `rerank=false` overrides. **Why now:** ADR 0008 Phase 1 and ADR 0015 Phases 1–2 are merged; ColBERT rerank works for `search_codebase`/`search_symbols` but `find_cross_references` and `map_service_dependencies` discard `colbert_vector`, leaving explicit Phase 1 test debt and inconsistent quality when `RERANK_ENABLED=true`. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** carried from Phase 1 — wire ColBERT into cross_reference/service_map search when rerank enabled; golden-set MRR with `--rerank`
- **Verify:** —
- **Git:** pending
- **Changelog:** yes — user-facing yes; entry at `verified` step

#### 2026-07-03 — merge
- **Phase / PR:** Phase 1 — optional ColBERT multivector reranking — [PR #1](https://github.com/Tusquito/codebase-indexer-mcp/pull/1)
- **Tracker status:** `merged`
- **Choices:** squash merge `891fb97` (10 commits on feature branch `adr/0008-phase-1-colbert-rerank`); ADR accepted as `Accepted (phase 1 — optional ColBERT multivector reranking)`; release skipped; phase 2+ deferred (adaptive rerank, per-tool overrides, cross_reference/service_map wiring)
- **Deviations:** none
- **Code evidence:** merged via PR #1 (`adr/0008-phase-1-colbert-rerank`)
- **Test debt:** carried from verification — ranx eval manual; colbert mismatch recreate; slow ColBERT opt-in only
- **Verify:** PR review round 2 approve; CI green; mergeable
- **Git:** [PR #1](https://github.com/Tusquito/codebase-indexer-mcp/pull/1) merged (squash `891fb97`)
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

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

### ADR 0015 — ColBERT HTTP sidecar

#### 2026-07-03 — merge
- **Phase / PR:** Phase 2 — GPU ColBERT worker image + index throughput benchmark vs CPU sidecar — [PR #3](https://github.com/Tusquito/codebase-indexer-mcp/pull/3)
- **Tracker status:** `merged`
- **Choices:** squash merge `b53029ed` on feature branch `adr/0015-phase-2-colbert-gpu`; ADR accept skipped (already Accepted); release skipped; phase 3+ deferred (MCP slim when remote-only)
- **Deviations:** none
- **Code evidence:** merged via PR #3 (`adr/0015-phase-2-colbert-gpu`)
- **Test debt:** carried from verification — Docker GPU image runtime smoke; live GPU embed integration beyond provider probe; `bench_colbert_sidecar --compare` unit test; host-side sidecar reachability docs
- **Verify:** carried from verification — pytest 236 passed, 3 skipped, 5 deselected; all in-scope plan requirements pass; review rounds: 1
- **Git:** [PR #3](https://github.com/Tusquito/codebase-indexer-mcp/pull/3) merged (squash `b53029ed`)
- **Changelog:** no — already added at verified step

#### 2026-07-03 — verification
- **Phase / PR:** Phase 2 — GPU ColBERT worker image + index throughput benchmark vs CPU sidecar
- **Tracker status:** `verified`
- **Choices:** GPU acceleration in sidecar image only (MCP stays CPU fastembed/onnxruntime); reuse `ColbertOnnxBackend` with `use_cuda`/`device_ids`; compose-only `COLBERT_GPU` doc flag; dedicated `bench_colbert_sidecar.py`; fail-fast CUDA startup; single-GPU 8GB OOM documented without auto-scheduler
- **Deviations:** none
- **Code evidence:** `colbert_worker/Dockerfile.gpu`, `docker-compose.colbert-worker.gpu.yml`, `mcp_server/src/codebase_indexer/colbert_worker/app.py`, `mcp_server/src/codebase_indexer/colbert_worker/settings.py`, `mcp_server/src/codebase_indexer/colbert_worker/cuda.py`, `mcp_server/src/codebase_indexer/indexer/backends/colbert_onnx.py`, `mcp_server/benchmarks/bench_colbert_sidecar.py`, `mcp_server/benchmarks/bench.py`, `docs/DEPLOYMENT.md`, `.env.example`, `.github/workflows/ci.yml`, `mcp_server/pyproject.toml`
- **Test debt:** Docker GPU image runtime smoke; live GPU embed integration beyond provider probe; `bench_colbert_sidecar --compare` unit test; host-side sidecar reachability docs
- **Verify:** tests run + plan compliance pass — pytest 236 passed, 3 skipped, 5 deselected; all in-scope plan requirements pass; review rounds: 1
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Phase 2 — GPU ColBERT worker image + index throughput benchmark vs CPU sidecar
- **Tracker status:** `implemented`
- **Choices:** `onnxruntime-gpu==1.26.0` pinned to match CPU lock; `python:3.12-slim` base with NVIDIA compose reservations mirroring Ollama GPU pattern; optional `COLBERT_DEVICE_IDS` env wired to `ColbertOnnxBackend.device_ids`; `/health` reports configured `device` + runtime `cuda_available`; fail-fast preload when CUDA requested but unavailable; dedicated `bench_colbert_sidecar.py` over full `run_benchmark` with remote ColBERT; single-GPU 8GB OOM documented (no auto-scheduler)
- **Deviations:** none
- **Code evidence:** `colbert_worker/Dockerfile.gpu`, `docker-compose.colbert-worker.gpu.yml`, `mcp_server/src/codebase_indexer/colbert_worker/app.py`, `mcp_server/src/codebase_indexer/colbert_worker/settings.py`, `mcp_server/src/codebase_indexer/indexer/backends/colbert_onnx.py`, `mcp_server/benchmarks/bench_colbert_sidecar.py`, `docs/DEPLOYMENT.md`, `.github/workflows/ci.yml`
- **Test debt:** Docker GPU image runtime smoke; live GPU embed integration; bench compare path unit test
- **Verify:** —
- **Git:** pending
- **Changelog:** no

#### 2026-07-03 — plan
- **Phase / PR:** Phase 2 — GPU ColBERT worker image + index throughput benchmark vs CPU sidecar
- **Tracker status:** `planned`
- **Choices:** GPU acceleration in **sidecar image only** (not MCP) to avoid fastembed/fastembed-gpu lock conflict and ADR 0011 CPU MCP model; reuse `ColbertOnnxBackend` with `use_cuda` rather than new backend class; compose-only `COLBERT_GPU` doc flag (like `OLLAMA_GPU`); dedicated benchmark script over full `run_benchmark` with `rerank_enabled=True` + `colbert_embed_backend=remote`; single PR for entire phase. **Chosen scope:** Optional GPU ColBERT sidecar via `colbert_worker/Dockerfile.gpu` (fastembed-gpu + onnxruntime-gpu, separate from MCP CPU deps); compose override `docker-compose.colbert-worker.gpu.yml` mirroring `docker-compose.ollama.gpu.yml`; compose-only `COLBERT_GPU` / `COLBERT_GPU_COUNT` and worker `COLBERT_USE_CUDA`; extend `ColbertOnnxBackend` + worker `/health` device reporting; dedicated `benchmarks/bench_colbert_sidecar.py` for remote-sidecar index throughput CPU vs GPU; CI-safe mocked/skipped GPU tests + non-blocking GPU Dockerfile CI job; `ColbertRemoteBackend` and HTTP contract unchanged
- **Assumptions:** Phase 1 merged (PR #2); operators use existing remote sidecar preset with `UPSERT_BATCH=10`; benchmark compares two sidecar deployments (CPU image vs GPU image) with same MCP/Qdrant/Ollama stack; NVIDIA Container Toolkit available for GPU override
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** CI-safe mocked/skipped GPU tests + non-blocking GPU Dockerfile CI job
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — merge
- **Phase / PR:** Phase 1 — HTTP sidecar + remote backend + compose override + tests + operator docs — [PR #2](https://github.com/Tusquito/codebase-indexer-mcp/pull/2)
- **Tracker status:** `merged`
- **Choices:** squash merge `e16dc59` on feature branch `adr/0015-phase-1-colbert-sidecar`; ADR accept skipped (already Accepted); release skipped; phase 2+ deferred (GPU worker; MCP slim when remote-only)
- **Deviations:** none
- **Code evidence:** merged via PR #2 (`adr/0015-phase-1-colbert-sidecar`)
- **Test debt:** carried from verification — optional slow onnx vs remote parity; operational memory-halt manual validation
- **Verify:** carried from verification — pytest 229 passed, 3 skipped; 45 targeted ColBERT tests passed; review rounds: 1
- **Git:** [PR #2](https://github.com/Tusquito/codebase-indexer-mcp/pull/2) merged (squash `e16dc59`)
- **Changelog:** no — already added at verified step

#### 2026-07-03 — verification
- **Phase / PR:** Phase 1 — HTTP sidecar + remote backend
- **Tracker status:** `verified`
- **Choices:** Opt-in `COLBERT_EMBED_BACKEND=remote` with `colbert_worker` sidecar; default remains in-process ONNX; sidecar uses FastAPI lifespan preload
- **Deviations:** none
- **Code evidence:** `colbert_worker/`, `colbert_worker/Dockerfile`, `colbert_remote.py`, `factory.py`, `config.py`, `embedder.py`, `docker-compose.colbert-worker.yml`, `.env.example`, `SEARCH_BEHAVIOR.md`
- **Test debt:** Optional slow onnx vs remote parity; operational memory-halt manual validation
- **Verify:** tests run + plan compliance pass — pytest 229 passed, 3 skipped; 45 targeted ColBERT tests passed; review rounds: 1
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Phase 1 — HTTP sidecar + remote backend + compose override + tests + operator docs
- **Tracker status:** `implemented`
- **Choices:** Mirror `OllamaDenseBackend` HTTP patterns; sidecar port 8082 internal-only; phase 1 no bearer auth; default `COLBERT_EMBED_BACKEND=onnx` unchanged; FastAPI lifespan for sidecar preload; shared `fastembed_cache` volume in compose override
- **Deviations:** Sidecar uses FastAPI lifespan instead of deprecated `on_event` startup for model preload
- **Code evidence:** `config.py`, `colbert_remote.py`, `factory.py`, `embedder.py`, `colbert_worker/`, `colbert_worker/Dockerfile`, `docker-compose.colbert-worker.yml`, `docker-compose.yml`, `.env.example`, `SEARCH_BEHAVIOR.md`, `test_colbert_remote_backend.py`, `test_colbert_worker.py`, `test_factory.py`, `test_config.py`
- **Test debt:** Optional slow onnx vs remote parity; compose E2E sidecar smoke; operational MCP memory regression; sidecar-unreachable preload error path
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — plan
- **Phase / PR:** Phase 1 — HTTP sidecar + remote backend + compose override + tests + operator docs
- **Tracker status:** `planned`
- **Choices:** Mirror `OllamaDenseBackend` HTTP patterns; sidecar port 8082 internal-only; phase 1 no bearer auth; one PR for entire phase. **Chosen scope:** `colbert_worker` FastAPI sidecar (GET /health, POST /v1/embed/colbert) reusing `ColbertOnnxBackend`; `ColbertRemoteBackend` httpx client mirroring `OllamaDenseBackend`; `create_colbert_backend()` selects onnx vs remote; config (`COLBERT_EMBED_BACKEND`, `COLBERT_URL`, `COLBERT_TIMEOUT`, `COLBERT_EMBED_BATCH_SIZE`); `embedder.py` release/idle without hardcoded `ColbertOnnxBackend` singleton when remote; `docker-compose.colbert-worker.yml` with shared `fastembed_cache`; tests; `.env.example` + `SEARCH_BEHAVIOR.md`; default `COLBERT_EMBED_BACKEND=onnx` unchanged
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** carry ADR 0008 phase 2+ test debt (xref/service_map rerank, golden MRR `--rerank`) as out-of-scope for this phase
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — prioritization
- **Phase / PR:** Phase 1 — HTTP sidecar + remote backend + compose override + tests + operator docs
- **Tracker status:** `candidate`
- **Choices:** Prioritize ADR 0015 Phase 1 over ADR 0008 phase 2+ refinements, Proposed ADR 0002 GraphRAG, and Proposed ADR 0014 recommendation tools; single phase per pipeline rule; mirror `OllamaDenseBackend` HTTP client pattern; default `COLBERT_EMBED_BACKEND=onnx` unchanged; no Qdrant schema or MAX_SIM rerank path changes. **Chosen scope:** `colbert_worker` FastAPI (GET /health, POST /v1/embed/colbert); `ColbertRemoteBackend` (httpx, batching, retries, preload); `create_colbert_backend()` onnx vs remote; config (`COLBERT_EMBED_BACKEND`, `COLBERT_URL`, `COLBERT_TIMEOUT`, `COLBERT_EMBED_BATCH_SIZE`); `embedder.py` release/idle without hardcoded `ColbertOnnxBackend` singleton; `docker-compose.colbert-worker.yml` with shared `fastembed_cache`; tests (`test_colbert_remote_backend.py`, `test_colbert_worker.py`, factory/config updates); `.env.example` sidecar preset + `SEARCH_BEHAVIOR.md` remote docs; defer GPU worker (P2) and MCP slim image (P3). **Why now:** ADR 0008 phase 1 ColBERT rerank is merged but in-process ONNX causes MCP RAM halt at `RERANK_ENABLED=true` on production-like deployments; ADR 0015 is Accepted and mirrors the proven Ollama dense HTTP split; prerequisites (0003, 0007, 0011, 0008 P1) are merged; no sidecar/remote backend code exists yet; opt-in default preserves existing deployments; validation path defined (mocked httpx tests, worker TestClient, config validation, operational memory criteria). **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** carry ADR 0008 phase 2+ test debt (xref/service_map rerank, golden MRR `--rerank`) as out-of-scope for this phase
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing unknown

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
| 5a–5b | PR review ↔ PR babysit (cloud) | — | no |
| 6 | Finisher (merge + accept + optional release) | `merged` + PR link | no |
| 7 | Git operator (cleanup) | — | no |

1. **Prioritization** — append log; summary row → `candidate`.
2. **Planning** — append log; summary row → `planned`; set chosen scope + user-facing flag.
3. **Implementation** — append log; summary row → `implemented`; code evidence + test debt.
3a–3b. **Review / fix loop** — invoker passes `## Review findings` (`Verdict: needs_fix`) to bug fix; passes `## ADR bug fix report` back to code review. Repeat until `Verdict: clean`. No tracker append during the loop.
4. **Verification** — when review is clean, apply Tracker append (`verified`); if user-facing, add CHANGELOG `[Unreleased]` bullet when applying the append.
5. **Git prepare** — feature branch `adr/NNNN-phase-N-<slug>`, grouped conventional commits, push, **PR into `main`**. No tracker append.
5a–5b. **PR review / babysit loop** — `adr-pr-review`; on `request_changes`, cloud `adr-pr-babysit` fixes branch; repeat until `approve` (max 5 rounds). No tracker append.
6. **Finish** — `adr-finisher` merges PR when gates pass, accepts ADR when eligible, optionally cuts CHANGELOG when version supplied; apply Tracker append (`merged`) with PR link.
7. **Cleanup** — `adr-git-operator` (`cleanup`) commits tracker on `main`, pushes, deletes merged feature branch, prunes remotes; workspace must be clean.

Apply steps 1–7 by passing each step's **Tracker append** output to the tracker update process (invoker or orchestrator).

### Orchestrator resume

When a phase is mid-pipeline (e.g. PR open or merged but tracker still `verified`), invoke **`adr-orchestrator`** with resume fields instead of restarting at prioritization:

```
Resume from: 6
ADR id: 0008
Phase / track: Phase 1
PR reference: #1
Release version: 0.4.0   # optional
```

Orchestrator bootstraps context from this tracker file + ADR index, re-runs PR review if needed, then runs **`adr-finisher`**.

## Open decisions queue

Decisions made during implementation that are **not** worth amending the ADR file — record here until promoted to a new ADR or the index status changes.

| Date | ADR | Question | Decision | Promote to ADR? |
|------|-----|----------|----------|-----------------|
| 2026-07-03 | 0008 | Accept ADR 0008 (Proposed → Accepted)? | `Accepted (phase 1 — optional ColBERT multivector reranking)` after PR #1 merge | no |
| 2026-07-03 | 0008 | Select `COLBERT_EMBED_MODEL` | `colbert-ir/colbertv2.0` (128-d per token) | no |
| 2026-07-03 | 0008 | Confirm operator re-index messaging for `RERANK_ENABLED=true` | Document in `.env.example` + `SEARCH_BEHAVIOR.md` | no |
| 2026-07-03 | 0008 | ADR `m=768` HNSW knob on `colbert` vector | Treat ADR prose as documentation error; `HnswConfigDiff(m=0)`; per-token `size` from registry | no |
| 2026-07-03 | 0008 | Cross-collection rerank ordering | Per-collection hybrid prefetch + ColBERT MAX_SIM rerank, then `fuse_cross_collection_rrf` | no |
| 2026-07-03 | 0008 | CI ColBERT test strategy | Synthetic multivectors in integration test; real model `@pytest.mark.slow` only | no |
| 2026-07-03 | 0008 | Index-time ColBERT embed ordering | Always sequential after dense+sparse when rerank enabled | no |
| 2026-07-03 | 0008 | Default rerank behavior | `RERANK_ENABLED=false` preserves existing hybrid RRF-only search | no |
| 2026-07-03 | 0008 | Slow real-model ColBERT test gate | `@pytest.mark.slow` + `RUN_SLOW_COLBERT=1` | no |
| 2026-07-03 | 0015 | Confirm phase 1 sidecar trust model | Internal network, no bearer auth — confirmed by ADR at plan | no |
| 2026-07-03 | 0015 | Lower `MCP_MEM_LIMIT` guidance after sidecar split? | Defer until operational validation | no |
| 2026-07-03 | 0015 | ADR 0008 phase 2+ test debt in this phase? | Out of scope — xref/service_map rerank, golden MRR `--rerank` remain 0008 P2+ | no |
| 2026-07-03 | 0015 | Sidecar model preload hook | FastAPI lifespan instead of deprecated `@app.on_event("startup")` | no |
| 2026-07-03 | 0015 | `onnxruntime-gpu` version pin | `onnxruntime-gpu==1.26.0` pinned to match CPU lock | no |
| 2026-07-03 | 0015 | Single-GPU VRAM sharing with Ollama | Single-GPU 8GB OOM documented; no auto-scheduler | no |
| 2026-07-03 | 0015 | Optional `device_ids` env for multi-GPU hosts | Optional `COLBERT_DEVICE_IDS` env wired to `ColbertOnnxBackend.device_ids` | no |
| 2026-07-03 | 0015 | Full-pipeline vs ColBERT-only microbench in benchmark output | Dedicated `bench_colbert_sidecar.py` over full `run_benchmark` with remote ColBERT | no |
| 2026-07-03 | 0015 | GPU compose toggle env var | Compose-only `COLBERT_GPU` doc flag (like `OLLAMA_GPU`); no MCP-side GPU deps | no |
| 2026-07-03 | 0015 | CUDA unavailable at sidecar startup | Fail-fast preload when CUDA requested but unavailable | no |
| 2026-07-03 | 0015 | MCP GPU deps for ColBERT | GPU acceleration in sidecar image only; MCP stays CPU fastembed/onnxruntime | no |
| 2026-07-03 | 0008 | xref/service_map `min_score` alignment when rerank enabled | Keep tool-specific internal `min_score` (0.3 / 0.25); ignored on hybrid/rerank via existing `qdrant.py` logic | no |
| 2026-07-03 | 0008 | Phase 2 track 1 search dispatch pattern | Shared `dispatch_search` helper in `search_common.py` (not duplicate colbert pass-through per tool) | no |
| 2026-07-03 | 0008 | xref semantic/import search dispatch | Route through `run_search()` (shared colbert-aware path) | no |
| 2026-07-03 | 0008 | service_map batched discovery rerank wiring | Route through `dispatch_search()` with pre-embedded colbert vectors | no |
| 2026-07-03 | 0008 | Order of remaining Phase 2 tracks (adaptive skip vs per-tool override) | Open — track 1 merged ([PR #4](https://github.com/Tusquito/codebase-indexer-mcp/pull/4)); decide at next prioritization | no |
| 2026-07-03 | 0008 | Accept Proposed 0002 or 0014 in a subsequent cycle for greenfield work? | Open — defer to next prioritization | no |
