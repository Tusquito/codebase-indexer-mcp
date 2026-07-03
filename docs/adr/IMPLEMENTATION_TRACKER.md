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
| [0002](0002-graphrag-neo4j-qdrant.md) | Optional GraphRAG (Neo4j + Qdrant) | Accepted (phase 1 — Neo4j storage + index-time graph writer) | Phase 1 — Neo4j storage + index-time graph writer | `merged` | Shipped: `storage/neo4j.py` async driver wrapper (neo4j driver 6.2.0) with idempotent schema; `indexer/graph_writer.py` writing ADR ontology from index batches (reuses `UrlExtractors`, `extract_build_deps`/`match_deps_to_collections`, public `extract_imported_names`); `pipeline.py` hooks mirroring Qdrant flush/delete cadence; best-effort graph errors to `PipelineResult.errors`; `context.py` optional `Neo4jStorage`; config (`GRAPH_ENABLED=false` default, `NEO4J_*`, `GRAPH_WRITER_BATCH`, `GRAPH_SCHEMA_VERSION=1`); `docker-compose.neo4j.yml` override only; mock driver CI unit tests; `.env.example` + `ARCHITECTURE.md`; no MCP tools Phase 1; endpoint `method` inference best-effort; defer Phase 2 Qdrant `graph_node_ids`, Phase 3 `expand_search_context`, Phase 4 Neo4j cross-project queries; [PR #10](https://github.com/Tusquito/codebase-indexer-mcp/pull/10) | 2026-07-03 |
| [0003](0003-hybrid-search-rrf-default.md) | Hybrid search RRF default | Accepted | all | `merged` | Shipped | 2026-07-02 |
| [0004](0004-collection-per-project-isolation.md) | Collection-per-project isolation | Accepted | all | `merged` | Shipped | 2026-07-02 |
| [0005](0005-mcp-retrieval-connector.md) | MCP retrieval connector | Accepted | all | `merged` | Shipped | 2026-07-02 |
| [0006](0006-explicit-fastembed-pipeline.md) | Explicit FastEmbed pipeline | Accepted | all | `merged` | Shipped | 2026-07-02 |
| [0007](0007-ranx-retrieval-evaluation.md) | Golden-set eval (ranx) | Accepted | all | `merged` | `eval_retrieval.py` + fixtures | 2026-07-02 |
| [0008](0008-optional-colbert-reranking.md) | Optional ColBERT reranking | Accepted | 1 | `merged` | Config (`RERANK_ENABLED=false` default, `COLBERT_EMBED_MODEL`, `RERANK_PREFETCH`, `RERANK_MAX_QUERY_TOKENS`); `ColbertOnnxBackend` via fastembed; multivector `colbert` + MAX_SIM rerank in `qdrant.py`; per-collection hybrid prefetch + ColBERT rerank then `fuse_cross_collection_rrf`; pipeline third embed pass (sequential); synthetic CI integration test + `@pytest.mark.slow` + `RUN_SLOW_COLBERT=1`; operator re-index docs; [PR #1](https://github.com/Tusquito/codebase-indexer-mcp/pull/1) | 2026-07-03 |
| [0008](0008-optional-colbert-reranking.md) | Optional ColBERT reranking | Accepted | 2 — track 1 (xref/service_map rerank wiring) | `merged` | Shared `dispatch_search()` in `search_common.py`; xref semantic/import via `run_search()`; service_map batched discovery via `dispatch_search()` with pre-embedded colbert vectors; tool-specific `min_score` retained (0.3 / 0.25); unit tests + `SEARCH_BEHAVIOR.md`; default deploy unchanged (`RERANK_ENABLED=false`); adaptive rerank and per-tool overrides deferred to track 2; [PR #4](https://github.com/Tusquito/codebase-indexer-mcp/pull/4) | 2026-07-03 |
| [0008](0008-optional-colbert-reranking.md) | Optional ColBERT reranking | Accepted | 2 — track 2a (adaptive rerank skip) | `merged` | `RERANK_ADAPTIVE_ENABLED=true`, `RERANK_ADAPTIVE_GAP=0.02`; hybrid RRF probe in `QdrantStorage._search_single` before ColBERT; probe limit `max(top_k, 2)`; fewer than 2 probe hits always runs ColBERT; `AdaptiveRerankStats` on storage for bench/eval skip-rate; ColBERT query embed unchanged; unit tests + `bench.py`/`eval_retrieval.py` skip-rate reporting; `SEARCH_BEHAVIOR.md` + `.env.example`; track 2b per-tool override deferred; default deploy unchanged (`RERANK_ENABLED=false`); [PR #6](https://github.com/Tusquito/codebase-indexer-mcp/pull/6) | 2026-07-03 |
| [0008](0008-optional-colbert-reranking.md) | Optional ColBERT reranking | Accepted | 2 — track 2b (per-tool `rerank=false` override) | `merged` | Per-tool `rerank: bool \| None = None` on `search_codebase`, `search_symbols`, xref/service_map semantic paths; embed + tool layer override (`use_rerank = self.rerank and rerank is not False`); `colbert_vector=None` skips storage rerank/adaptive paths; `rerank=None` default; `rerank=false` skips ColBERT when `RERANK_ENABLED=true`; import-phrased xref inherits tool-level `rerank`; exact symbol/call_site unaffected; `recommend_code` excluded; final ADR 0008 phase complete; test debt: Embedder rerank unit tests, adaptive+override integration, golden-set `rerank=false` sweep, live Qdrant adaptive (carried from 2a); [PR #7](https://github.com/Tusquito/codebase-indexer-mcp/pull/7) | 2026-07-03 |
| [0009](0009-multi-hop-retrieval-strategies.md) | Multi-hop retrieval | Accepted (phase 1) | 1 | `merged` | Client decomposition docs + golden tags | 2026-07-02 |
| [0009](0009-multi-hop-retrieval-strategies.md) | Multi-hop retrieval | Accepted (phase 1; phase 2 merged) | 2 — automated 2-hop client eval script | `merged` | `eval_multihop.py` + `multihop_rrf.fuse_hop_rrf`; curated `hop2_query_text` inline in `golden_queries.jsonl`; client-side RRF fusion hop 1 + hop 2 via `run_search`; `--rerank` passthrough; side-by-side ranx vs single-pass on `multi_hop` slice; `eval_baseline.json` `multi_hop_2hop` snapshot (live verify, nomic embed); unit tests; `SEARCH_BEHAVIOR.md` + `ARCHITECTURE.md`; no MCP/compose/runtime changes; defer server-side hop fusion and GraphRAG to ADR 0002+; [PR #8](https://github.com/Tusquito/codebase-indexer-mcp/pull/8) | 2026-07-03 |
| [0010](0010-defer-ragas-to-client.md) | Defer Ragas to client | Accepted | all | `merged` | Export script + DEPLOYMENT guide | 2026-07-02 |
| [0011](0011-ollama-only-dense-embedding.md) | Ollama-only dense embedding | Accepted | all | `merged` | See CHANGELOG [Unreleased] | 2026-07-02 |
| [0012](0012-retrieval-only-rag-split.md) | Retrieval-only RAG split | Accepted | all | `merged` | Shipped | 2026-07-02 |
| [0013](0013-external-agent-knowledge-base.md) | External agent knowledge base | Accepted | all | `merged` | MCP tools surface | 2026-07-02 |
| [0014](0014-vector-discovery-and-ops-automation.md) | Vector discovery + n8n ops | Accepted (phase 1 — recommendation search tool) | Track A — Phase 1 (Recommendation search tool) | `merged` | Tool `recommend_code`; `QdrantStorage.recommend`; config (`RECOMMEND_ENABLED`, `RECOMMEND_MAX_EXAMPLES`); RecommendStrategy AVERAGE_VECTOR; dense-only; path_glob fnmatch + limit×3; missing chunk IDs fail fast; single-collection; defer outlier helper (Track A P2), n8n compose (Track B), sparse fusion, multi-collection; [PR #5](https://github.com/Tusquito/codebase-indexer-mcp/pull/5) | 2026-07-03 |
| [0014](0014-vector-discovery-and-ops-automation.md) | Vector discovery + n8n ops | Accepted (phase 1; phase 2 — outlier / diversity helper) | Track A — Phase 2 (outlier / diversity helper) | `merged` | Tool `find_outlier_chunks`; `QdrantStorage.find_outlier_chunks`; `RecommendStrategy.BEST_SCORE` negative-only; cosine-to-centroid + `OUTLIER_MAX_SIMILARITY` (0.55); gate via `RECOMMEND_ENABLED` (no `OUTLIER_ENABLED`); `OUTLIER_MAX_CONTEXT_SAMPLES` (200); scroll supplement only when `path_glob` or no explicit `context_chunk_ids`; bounded `limit` (cap 20); dense-only single-collection; defer sparse fusion, multi-collection, Track B n8n, Discovery API context pairs; [PR #9](https://github.com/Tusquito/codebase-indexer-mcp/pull/9) | 2026-07-03 |
| [0015](0015-colbert-http-sidecar.md) | ColBERT HTTP sidecar | Accepted | 1 | `merged` | Opt-in `COLBERT_EMBED_BACKEND=remote` + `colbert_worker` sidecar; default in-process ONNX unchanged; FastAPI lifespan preload; `ColbertRemoteBackend` httpx client; `docker-compose.colbert-worker.yml` with shared `fastembed_cache`; `.env.example` + `SEARCH_BEHAVIOR.md`; [PR #2](https://github.com/Tusquito/codebase-indexer-mcp/pull/2) | 2026-07-03 |
| [0015](0015-colbert-http-sidecar.md) | ColBERT HTTP sidecar | Accepted | 2 | `merged` | GPU sidecar via `colbert_worker/Dockerfile.gpu` (`onnxruntime-gpu==1.26.0`, `python:3.12-slim`); compose override `docker-compose.colbert-worker.gpu.yml` (NVIDIA reservations mirroring Ollama); `COLBERT_DEVICE_IDS` → `ColbertOnnxBackend.device_ids`; worker `/health` reports `device` + `cuda_available`; fail-fast CUDA preload; `bench_colbert_sidecar.py` remote throughput bench; single-GPU 8GB OOM documented (no auto-scheduler); CI-safe mocked/skipped GPU tests + non-blocking GPU Dockerfile CI job; [PR #3](https://github.com/Tusquito/codebase-indexer-mcp/pull/3) | 2026-07-03 |
| [0015](0015-colbert-http-sidecar.md) | ColBERT HTTP sidecar | Accepted | 3+ | `not_started` | MCP slim image when remote-only | — |
| [0017](0017-model-tokenizer-ollama-dense-truncation.md) | Model-accurate tokenizer for Ollama dense truncation | Accepted (phase 1 — loader + Ollama backend) | Phase 1 — loader + Ollama backend | `merged` | `load_dense_tokenizer(model_id)` in `tokenizer_loader.py` via `tokenizers.Tokenizer.from_pretrained` + HF env cache dirs; shared class-level `Tokenizer` in `OllamaDenseBackend` at `preload()` via `_ensure_truncation()`; `_truncate_batch` uses `truncate_for_embedding` (sparse BM25 path untouched); fallback = log WARNING + pass text through unchanged; unit tests (mock + optional slow Nomic); `ARCHITECTURE.md`, `.env.example`, `docker-compose.yml` HF_HOME; defer Phase 2 observability + ADR 0011 body edit; [PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11) | 2026-07-03 |
| [0016](0016-qwen3-embedding-default-dense-model.md) | Adopt Qwen3-Embedding-4B as default Ollama dense model | Accepted (phase 1 — config, Ollama MRL, docs, tests) | Phase 1 — Config, Ollama MRL, docs, tests | `merged` | Qwen3 0.6B/4B/8B in `KNOWN_EMBED_MODEL_*` (max tokens 32768); MRL `dimensions` passthrough (32≤size≤native) in `OllamaDenseBackend` + `factory.py`; Qwen3 GPU defaults in `.env.example`; compose generator Qwen3 (`scripts/run_compose_integration.py`); `benchmarks/_settings.py`; unit tests; docs; defer Phase 2 eval baseline + `num_ctx`; generator-only compose env; [PR #12](https://github.com/Tusquito/codebase-indexer-mcp/pull/12) | 2026-07-03 |
| [0018](0018-telemetry-observability-otel-prometheus.md) | Adopt OpenTelemetry instrumentation with Prometheus metrics and optional OTLP export | Accepted (phase 1 — Application Prometheus metrics (MCP + ColBERT worker)) | Phase 1 — Application Prometheus metrics (MCP + ColBERT worker) | `merged` | Opt-in `METRICS_ENABLED=false` default; `prometheus_client` on dedicated `CollectorRegistry`; metrics-only `@observe_tool` on all MCP tool handlers; no collection/rel_path labels; application counters/histograms + truncation counter; index metrics via IndexJobTracker; `GET /metrics` on MCP and ColBERT worker HTTP layer; unit tests (`test_telemetry_metrics.py`); `DEPLOYMENT.md` scrape docs; defer `METRICS_PORT`, docker-compose scrape wiring, Phase 2 OTel traces, Phase 3 observability compose stack; [PR #13](https://github.com/Tusquito/codebase-indexer-mcp/pull/13) | 2026-07-03 |

Superseded [0001](0001-pluggable-embed-backends.md) — historical; implementation superseded by [0011](0011-ollama-only-dense-embedding.md).

## Active and upcoming work

### Partial acceptance

| ADR | Done | Remaining |
|-----|------|-----------|
| 0002 | Phase 1 — Neo4j storage + index-time graph writer ([PR #10](https://github.com/Tusquito/codebase-indexer-mcp/pull/10)) | Phases 2–4 (Qdrant `graph_node_ids`, `expand_search_context`, Neo4j cross-project queries) |
| 0014 | Track A Phase 1 — recommendation search tool ([PR #5](https://github.com/Tusquito/codebase-indexer-mcp/pull/5)); Track A Phase 2 — outlier helper ([PR #9](https://github.com/Tusquito/codebase-indexer-mcp/pull/9)) | Track B (n8n compose) deferred |
| 0009 | Phase 1 — `SEARCH_BEHAVIOR.md` multi-hop section, golden `multi_hop` tags; Phase 2 — automated 2-hop client eval script ([PR #8](https://github.com/Tusquito/codebase-indexer-mcp/pull/8)) | Phase 3+ server mechanisms; optional graph-backed hops per [0002](0002-graphrag-neo4j-qdrant.md) |
| 0015 | Phase 1 — HTTP sidecar + remote backend ([PR #2](https://github.com/Tusquito/codebase-indexer-mcp/pull/2)); Phase 2 — GPU worker + benchmark ([PR #3](https://github.com/Tusquito/codebase-indexer-mcp/pull/3)) | MCP slim image when remote-only (phase 3+) |
| 0017 | Phase 1 — loader + Ollama backend ([PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)) | Phase 2 observability + ADR 0011 body edit |
| 0016 | Phase 1 — config, Ollama MRL, docs, tests ([PR #12](https://github.com/Tusquito/codebase-indexer-mcp/pull/12)) | Phase 2 eval baseline refresh (`eval_baseline.json`, `multi_hop_2hop` snapshot) |
| 0018 | Phase 1 — Application Prometheus metrics (MCP + ColBERT worker) ([PR #13](https://github.com/Tusquito/codebase-indexer-mcp/pull/13)) | Phase 2 OTel traces; Phase 3 observability compose stack; `METRICS_PORT`, docker-compose scrape wiring |

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
- **Phase / PR:** Phase 2 — track 2b (per-tool `rerank=false` override) — [PR #7](https://github.com/Tusquito/codebase-indexer-mcp/pull/7)
- **Tracker status:** `merged`
- **Choices:** squash merge PR #7 on feature branch `adr/0008-phase-2-track-2b-rerank-override`; ADR 0008 accepted as full **Accepted** status; final ADR 0008 phase complete; release skipped
- **Deviations:** none
- **Code evidence:** merged via PR #7 (`adr/0008-phase-2-track-2b-rerank-override`; squash `00f4c3e4fcc3efe4d81936e6025dab41d05e08f9`)
- **Test debt:** carried from verification — direct Embedder rerank unit tests; adaptive + per-tool override integration; golden-set `rerank=false` quality sweep; live Qdrant adaptive integration (carried from track 2a)
- **Verify:** carried from verification — 23 targeted + 264 unit tests pass; plan compliance pass; integration skipped per plan; review rounds: 1
- **Git:** [PR #7](https://github.com/Tusquito/codebase-indexer-mcp/pull/7) merged (squash `00f4c3e4fcc3efe4d81936e6025dab41d05e08f9`)
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — verification
- **Phase / PR:** Phase 2 — track 2b (per-tool `rerank=false` override)
- **Tracker status:** `verified`
- **Choices:** Override at embed + tool layer (`use_rerank = self.rerank and rerank is not False`); `colbert_vector=None` skips storage rerank/adaptive paths; `rerank=None` default; xref import-phrased search inherits tool-level `rerank`; exact symbol / call_site unaffected; `recommend_code` excluded; final ADR 0008 phase
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/indexer/embedder.py`, `mcp_server/src/codebase_indexer/tools/search_common.py`, `mcp_server/src/codebase_indexer/tools/search.py`, `mcp_server/src/codebase_indexer/tools/symbols.py`, `mcp_server/src/codebase_indexer/tools/cross_references.py`, `mcp_server/src/codebase_indexer/tools/service_map.py`, `mcp_server/src/codebase_indexer/main.py`, `docs/SEARCH_BEHAVIOR.md`, `.env.example`
- **Test debt:** direct Embedder rerank unit tests; adaptive + per-tool override integration; golden-set `rerank=false` quality sweep; live Qdrant adaptive integration (carried from track 2a)
- **Verify:** 23 targeted + 264 unit tests pass; plan compliance pass; integration skipped per plan; review rounds: 1
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Phase 2 — track 2b (per-tool `rerank=false` override)
- **Tracker status:** `implemented`
- **Choices:** Override at embed + tool layer (not new storage flag); `use_rerank = self.rerank and rerank is not False`; `rerank=false` only effective when global `RERANK_ENABLED=true`; `rerank=None` preserves current behavior; `rerank=true` does not enable ColBERT without global flag or bypass adaptive skip; import-phrased xref search inherits tool-level `rerank`; exact symbol / call_site paths unaffected
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/indexer/embedder.py`, `mcp_server/src/codebase_indexer/tools/search_common.py`, `mcp_server/src/codebase_indexer/tools/search.py`, `mcp_server/src/codebase_indexer/tools/symbols.py`, `mcp_server/src/codebase_indexer/tools/cross_references.py`, `mcp_server/src/codebase_indexer/tools/service_map.py`, `mcp_server/src/codebase_indexer/main.py`, `mcp_server/tests/test_search_common.py`, `mcp_server/tests/test_search_tools.py`, `mcp_server/tests/test_cross_references.py`, `mcp_server/tests/test_service_map.py`, `docs/SEARCH_BEHAVIOR.md`, `.env.example`
- **Test debt:** direct Embedder rerank unit tests; adaptive + per-tool override integration; golden-set `rerank=false` quality sweep; live Qdrant adaptive integration (carried from track 2a)
- **Verify:** —
- **Git:** pending
- **Changelog:** no

#### 2026-07-03 — plan
- **Phase / PR:** Phase 2 — track 2b (per-tool `rerank=false` override)
- **Tracker status:** `planned`
- **Choices:** Implement override at embed + tool layer (not new storage flag); `rerank=false` only effective when `RERANK_ENABLED=true`; `rerank=None` preserves current behavior; adaptive skip (track 2a) unchanged when effective rerank is on; single PR; no compose/env changes; final ADR 0008 phase. **Chosen scope:** Optional `rerank: bool | None = None` on `search_codebase`, `search_symbols`, and semantic search paths in `find_cross_references` / `map_service_dependencies`; thread through `run_search` and `Embedder.embed_query` / `embed_queries` so `rerank=false` skips ColBERT query embed and MAX_SIM (via `colbert_vector=None`); unit tests per tool + `test_search_common`; `SEARCH_BEHAVIOR.md` + `.env.example` + `main.py` instructions; defer golden-set adaptive gap sweep and live Qdrant adaptive integration test debt from track 2a
- **Assumptions:** `QdrantStorage._search_single` already skips rerank when `colbert_vector is None`; `rerank=true` cannot enable ColBERT without global flag and indexed multivectors; payload-only xref paths (exact symbol, call_site) unaffected
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** unit tests per tool + `test_search_common`; defer golden-set adaptive gap sweep and live Qdrant adaptive integration test debt from track 2a
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — prioritization
- **Phase / PR:** Phase 2 — track 2b (per-tool `rerank=false` override)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0008 Phase 2 track 2b over 0014 Track A P2 outlier helper and Proposed 0002 GraphRAG Phase 1 (tie on weighted score; lower scope/risk tie-breaker); over 0009 Phase 2 automated 2-hop eval script (closest alternative, −0.5 weighted score, benchmark-only); over 0015 Phase 3 slim image and 0014 Track B n8n (deferred twice / ops-only); single phase per pipeline rule; no ADR Accept required (0008 already Accepted); complete ColBERT Improve Search arc before greenfield GraphRAG or discovery P2. **Chosen scope:** Optional `rerank: bool | None = None` on `search_codebase`, `search_symbols`, and semantic search paths in `find_cross_references` / `map_service_dependencies`; thread through `run_search` / `dispatch_search` / `QdrantStorage.search` to skip ColBERT query embed and MAX_SIM when `rerank=false`; unit tests per tool; `SEARCH_BEHAVIOR.md` + `.env.example` documentation; defer golden-set adaptive gap sweep and live Qdrant adaptive integration test debt from track 2a. **Why now:** ColBERT arc merged through Phase 1, Phase 2 tracks 1 and 2a, and ADR 0015 Phases 1–2; track 2b is the sole remaining ADR 0008 Phase 2 item explicitly deferred after track 2a ([PR #6](https://github.com/Tusquito/codebase-indexer-mcp/pull/6)); MCP search tools lack per-call rerank control while global `RERANK_ENABLED=true` always embeds ColBERT and runs MAX_SIM; prerequisites (0003, 0007, 0011, 0015) satisfied; measurable via `eval_retrieval --rerank` and unit tests; no new mandatory infra; default deploy unchanged when `RERANK_ENABLED=false`. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** defer golden-set adaptive gap sweep and live Qdrant adaptive integration test debt from track 2a
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — merge
- **Phase / PR:** Phase 2 — track 2a (adaptive rerank skip) — [PR #6](https://github.com/Tusquito/codebase-indexer-mcp/pull/6)
- **Tracker status:** `merged`
- **Choices:** squash merge `1411060` on feature branch `adr/0008-phase-2-track-2a-adaptive-rerank-skip`; ADR accept updated to `Accepted (phase 1; phase 2 tracks 1, 2a merged)`; release skipped; track 2b per-tool override deferred
- **Deviations:** none
- **Code evidence:** merged via PR #6 (`adr/0008-phase-2-track-2a-adaptive-rerank-skip`)
- **Test debt:** carried from verification — optional dedicated test for single-probe-hit ColBERT path; live Qdrant adaptive integration test; golden-set gap threshold sweep
- **Verify:** carried from verification — 53 targeted tests passed; 265-suite passed; ruff 1× F401 suggestion (unused import)
- **Git:** [PR #6](https://github.com/Tusquito/codebase-indexer-mcp/pull/6) merged (squash `1411060`)
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — verification
- **Phase / PR:** Phase 2 — track 2a (adaptive rerank skip)
- **Tracker status:** `verified`
- **Choices:** `RERANK_ADAPTIVE_ENABLED=true`, `RERANK_ADAPTIVE_GAP=0.02` when rerank on; hybrid RRF probe in `_search_single` with probe limit `max(top_k, 2)`; fewer than 2 probe hits always runs ColBERT; `AdaptiveRerankStats` for bench/eval skip-rate; ColBERT query embed unchanged; track 2b per-tool override deferred
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_qdrant_search.py`, `mcp_server/tests/test_benchmarks.py`, `mcp_server/benchmarks/bench.py`, `mcp_server/benchmarks/eval_retrieval.py`, `docs/SEARCH_BEHAVIOR.md`, `.env.example`
- **Test debt:** optional dedicated test for single-probe-hit ColBERT path; live Qdrant adaptive integration test; golden-set gap threshold sweep
- **Verify:** tests run + plan compliance pass — 53 targeted tests passed; 265-suite passed; ruff 1× F401 suggestion (unused import)
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Phase 2 — track 2a (adaptive rerank skip)
- **Tracker status:** `implemented`
- **Choices:** Shipped `RERANK_ADAPTIVE_ENABLED=true` and `RERANK_ADAPTIVE_GAP=0.02`; adaptive logic in `QdrantStorage._search_single` via hybrid RRF probe before ColBERT; probe limit `max(top_k, 2)`; fewer than 2 probe hits always runs ColBERT; `AdaptiveRerankStats` counters on storage for bench/eval skip-rate; ColBERT query embed in `Embedder.embed_query` unchanged; track 2b per-tool override deferred; default deploy unchanged (`RERANK_ENABLED=false`)
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_qdrant_search.py`, `mcp_server/benchmarks/bench.py`, `mcp_server/benchmarks/eval_retrieval.py`, `mcp_server/tests/test_benchmarks.py`, `docs/SEARCH_BEHAVIOR.md`, `.env.example`
- **Test debt:** golden-set gap sweep via `eval_retrieval --rerank`; optional live Qdrant integration test for adaptive skip; multi-collection adaptive + global RRF unit test; track 2b per-tool `rerank=false` deferred
- **Verify:** —
- **Git:** pending
- **Changelog:** no

#### 2026-07-03 — plan
- **Phase / PR:** Phase 2 — track 2a (adaptive rerank skip)
- **Tracker status:** `planned`
- **Choices:** Single PR for track 2a; adaptive logic in `QdrantStorage._search_single` via hybrid RRF probe before ColBERT; new env vars `RERANK_ADAPTIVE_ENABLED` + `RERANK_ADAPTIVE_GAP`; `AdaptiveRerankStats` counters on storage for bench/eval skip-rate; recommended defaults `RERANK_ADAPTIVE_ENABLED=true`, `RERANK_ADAPTIVE_GAP=0.02` pending golden-set sweep; ColBERT query embed in `Embedder.embed_query` unchanged in track 2a; track 2b per-tool override explicitly deferred; no ADR Accept required (0008 already Accepted). **Chosen scope:** Configurable adaptive ColBERT skip when hybrid prefetch top-1 vs top-2 RRF score gap exceeds threshold; implement in `QdrantStorage.search` rerank path; unit tests; `bench.py`/`eval_retrieval.py` skip-rate and P95 reporting; `SEARCH_BEHAVIOR.md` + `.env.example` docs; defer per-tool MCP `rerank=false` parameter override to track 2b
- **Assumptions:** Gap measured per-collection on Qdrant RRF fusion scores; multi-collection search applies adaptive per `_search_single` then existing `fuse_cross_collection_rrf`; default deploy unchanged when `RERANK_ENABLED=false`; prerequisites ADR 0003/0007/0011/0015 and 0008 P1 + P2 track 1 satisfied in code
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** unit tests for adaptive skip logic; `bench.py`/`eval_retrieval.py` skip-rate and P95 reporting; `SEARCH_BEHAVIOR.md` + `.env.example` docs; optional live Qdrant integration test vs unit mocks only (open)
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — prioritization
- **Phase / PR:** Phase 2 — track 2a (adaptive rerank skip)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0008 Phase 2 track 2a over 0009 Phase 2 automated 2-hop eval script (closest alternative, −1.5 weighted score but benchmark-only), 0014 Track A P2 outlier helper, Proposed 0002 GraphRAG Phase 1, 0015 Phase 3 slim image, and 0014 Track B n8n; single phase per pipeline rule; adaptive skip before per-tool override; no ADR Accept required (0008 already Accepted). **Chosen scope:** Configurable adaptive ColBERT skip when hybrid prefetch top-1 vs top-2 RRF score gap exceeds threshold; implement in `QdrantStorage.search` rerank path; unit tests; `bench.py`/`eval_retrieval.py` skip-rate and P95 reporting; `SEARCH_BEHAVIOR.md` + `.env.example` docs; defer per-tool MCP `rerank=false` parameter override to track 2b. **Why now:** ColBERT arc merged (0008 P1, 0015 P1–P2, 0008 P2 track 1, 0014 P1); rerank wired on all search tools but ADR 0008 deferred adaptive skip and per-tool overrides; no adaptive code in repo; ARCHITECTURE.md lists this as remaining Improve Search work; prerequisites (0003, 0007, 0011, 0015) satisfied; measurable via `eval_retrieval --rerank` and `bench.py`; no new mandatory infra; default deploy unchanged. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** —
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing unknown

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

#### 2026-07-03 — merge
- **Phase / PR:** Phase 1 — Neo4j storage + index-time graph writer — [PR #10](https://github.com/Tusquito/codebase-indexer-mcp/pull/10)
- **Tracker status:** `merged`
- **Choices:** squash merge `c511c6f` on feature branch `adr/0002-phase-1-neo4j-graph-writer`; ADR accepted as `Accepted (phase 1 — Neo4j storage + index-time graph writer)` (docs commit `a48dd97`); release skipped; Phases 2–4 deferred
- **Deviations:** none
- **Code evidence:** merged via PR #10 (`adr/0002-phase-1-neo4j-graph-writer`; squash `c511c6f`)
- **Test debt:** carried from verification — live Neo4j incremental delete integration; compose override smoke; graph-failure-during-index scenario; pipeline-level delete hook assertion
- **Verify:** carried from verification — 17 graph unit tests pass + plan compliance pass; Docker integration pass per integration report; review rounds: 1
- **Git:** [PR #10](https://github.com/Tusquito/codebase-indexer-mcp/pull/10) merged (squash `c511c6f`)
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — verification
- **Phase / PR:** Phase 1 — Neo4j storage + index-time graph writer
- **Tracker status:** `verified`
- **Choices:** Mock-driver CI default; best-effort graph errors to `PipelineResult.errors`; neo4j driver 6.2.0; endpoint `method` inference best-effort; compose override only; no MCP tools Phase 1
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/storage/neo4j.py`, `mcp_server/src/codebase_indexer/indexer/graph_writer.py`, `mcp_server/src/codebase_indexer/indexer/pipeline.py`, `mcp_server/src/codebase_indexer/context.py`, `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/tools/index.py`, `docker-compose.neo4j.yml`, `.env.example`, `docs/ARCHITECTURE.md`
- **Test debt:** Live Neo4j incremental delete integration; compose override smoke; graph-failure-during-index scenario; pipeline-level delete hook assertion
- **Verify:** 17 graph unit tests pass + plan compliance pass; Docker integration pass per integration report; review rounds: 1
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Phase 1 — Neo4j storage + index-time graph writer
- **Tracker status:** `implemented`
- **Choices:** mock driver CI; best-effort graph errors to `PipelineResult.errors`; BUILD_DEPENDS via on-disk re-read; Neo4j compose override only; no MCP tools Phase 1; public `extract_imported_names` in chunker
- **Deviations:** neo4j driver resolved to 6.2.0 (5.x planned); endpoint `method` inference best-effort only
- **Code evidence:** `mcp_server/pyproject.toml`, `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/neo4j.py`, `mcp_server/src/codebase_indexer/indexer/graph_writer.py`, `mcp_server/src/codebase_indexer/indexer/chunker.py`, `mcp_server/src/codebase_indexer/indexer/pipeline.py`, `mcp_server/src/codebase_indexer/context.py`, `mcp_server/src/codebase_indexer/tools/index.py`, `mcp_server/src/codebase_indexer/main.py`, `docker-compose.neo4j.yml`, `.env.example`, `docs/ARCHITECTURE.md`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_neo4j_storage.py`, `mcp_server/tests/test_graph_writer.py`, `mcp_server/tests/test_pipeline_graph.py`
- **Test debt:** live Neo4j incremental delete integration; compose override smoke; graph-failure-during-index scenario
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — plan
- **Phase / PR:** Phase 1 — Neo4j storage + index-time graph writer
- **Tracker status:** `planned`
- **Choices:** Single PR for entire Phase 1; default CI uses bolt/session mocks (not Testcontainers); graph write errors append to `PipelineResult.errors` while Qdrant upsert succeeds; manifest `BUILD_DEPENDS` via on-disk re-read for full file content; promote `_extract_imported_names` to public API; Neo4j only in compose override (not base `docker-compose.yml`); no new MCP tools in Phase 1. **Chosen scope:** `storage/neo4j.py` async driver wrapper with idempotent schema (`Chunk.chunk_id`, `File(collection,rel_path)`, `Symbol.qualified_name`, `Endpoint`, `Collection` constraints); `indexer/graph_writer.py` writing ADR ontology from index batches reusing `UrlExtractors`, `extract_build_deps`/`match_deps_to_collections`, and public `extract_imported_names` from chunker; `pipeline.py` hooks mirroring Qdrant flush/delete cadence; `context.py` optional `Neo4jStorage`; config (`GRAPH_ENABLED=false` default, `NEO4J_*`, `GRAPH_WRITER_BATCH`, `GRAPH_SCHEMA_VERSION=1`); optional `docker-compose.neo4j.yml`; unit tests (mock driver CI + optional slow live Neo4j); `.env.example` + `ARCHITECTURE.md`; defer Phase 2 Qdrant `graph_node_ids`, Phase 3 `expand_search_context`, Phase 4 Neo4j cross-project queries. **Requires formal Accept of Proposed ADR 0002 before implementation.**
- **Assumptions:** `neo4j` Python driver 5.x; Neo4j Community 5 in compose; collection name = folder basename; full re-index required when enabling graph on existing collections; prerequisites ADR 0003/0004/0005/0009 satisfied in code
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** unit tests (mock driver CI + optional slow live Neo4j); Testcontainers vs mock-only CI open (recommend mock default)
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — prioritization
- **Phase / PR:** Phase 1 — Neo4j storage + index-time graph writer
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0002 Phase 1 over 0009 eval_multihop CI gate (closest alternative, −1.0 weighted score, benchmark-only; tie within ~10% but lower unlock); over 0008 test-debt closure PR (QA-only, no capability); over 0015 Phase 3 slim image and 0014 Track B n8n (ops-only, deferred twice); single phase per pipeline rule; begin GraphRAG foundation after Improve Search + vector discovery arcs complete. **Chosen scope:** `storage/neo4j.py` async driver wrapper; `indexer/graph_writer.py` reusing chunk/xref/build extractors; `pipeline.py` post-flush invocation; config (`GRAPH_ENABLED`, `NEO4J_*`, `GRAPH_WRITER_BATCH`); optional `docker-compose.neo4j.yml`; idempotent Neo4j constraints/indexes; unit tests per ADR Validation §Phase 1; `.env.example` + `ARCHITECTURE.md` sync; defer Phase 2 payload linking, Phase 3 `expand_search_context`, Phase 4 Neo4j cross-project queries. **Requires formal Accept of Proposed ADR 0002 before implementation.** **Why now:** ColBERT arc (0008 all phases, 0015 P1–P2), vector discovery Track A (0014 P1–P2), and multi-hop client eval (0009 Phase 2) are merged; ADR 0002 is the sole Proposed ADR and the largest remaining capability gap for structural multi-hop queries; ADR 0009 and 0013 explicitly defer graph-backed retrieval to 0002; no `GRAPH_ENABLED`/Neo4j code exists (`config.py` grep empty, no `storage/neo4j.py`); Phase 1 is opt-in (`GRAPH_ENABLED=false` default) with defined Testcontainers/bolt-mock validation; unlocks Phases 2–4 and 0009 server-side graph expansion path. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** no `GRAPH_ENABLED`/Neo4j code exists (`config.py` grep empty, no `storage/neo4j.py`)
- **Test debt:** —
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing unknown

---

### ADR 0009 — Multi-hop retrieval

#### 2026-07-03 — merge
- **Phase / PR:** Phase 2 — automated 2-hop client eval script — [PR #8](https://github.com/Tusquito/codebase-indexer-mcp/pull/8)
- **Tracker status:** `merged`
- **Choices:** squash merge `b101be6` on feature branch `adr/0009-phase-2-multihop-eval` (deleted post-merge); ADR accepted as `Accepted (phase 1; phase 2 merged)` (commit `d761d09` on main); release skipped
- **Deviations:** none
- **Code evidence:** merged via PR #8 (`adr/0009-phase-2-multihop-eval`; squash `b101be6`)
- **Test debt:** carried from verification — no CI gate for `eval_multihop`; baseline snapshot not aligned to jina embed model; no unit test for `compare_vs_baseline()`
- **Verify:** carried from verification — 20 unit tests pass + plan compliance pass; Docker integration skipped per plan; review rounds: 1
- **Git:** [PR #8](https://github.com/Tusquito/codebase-indexer-mcp/pull/8) merged (squash `b101be6`); branch `adr/0009-phase-2-multihop-eval` deleted post-merge
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — verification
- **Phase / PR:** Phase 2 — automated 2-hop client eval script
- **Tracker status:** `verified`
- **Choices:** Separate `eval_multihop.py` CLI; curated `hop2_query_text` inline in golden fixture; RRF in `multihop_rrf.fuse_hop_rrf`; `--rerank` passthrough included; `multi_hop_2hop` baseline from live verify (nomic embed, not jina)
- **Deviations:** none
- **Code evidence:** `mcp_server/benchmarks/eval_multihop.py`, `mcp_server/benchmarks/multihop_rrf.py`, `mcp_server/benchmarks/eval_retrieval.py`, `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `mcp_server/benchmarks/fixtures/eval_baseline.json`, `mcp_server/tests/test_multihop_rrf.py`, `mcp_server/tests/test_eval_multihop.py`, `docs/SEARCH_BEHAVIOR.md`, `docs/ARCHITECTURE.md`
- **Test debt:** No CI gate for `eval_multihop`; baseline snapshot not aligned to jina embed model; no unit test for `compare_vs_baseline()`
- **Verify:** 20 unit tests pass + plan compliance pass; Docker integration skipped per plan; review rounds: 1
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Phase 2 — automated 2-hop client eval script
- **Tracker status:** `implemented`
- **Choices:** Separate `eval_multihop.py` CLI; curated `hop2_query_text` inline in `golden_queries.jsonl`; RRF fusion in `benchmarks/multihop_rrf.fuse_hop_rrf`; `--rerank` passthrough included; `multi_hop_2hop` baseline block added after live verify
- **Deviations:** Live baseline snapshot used local nomic embed model (not baseline jina model); ADR Accept/index update deferred to merge gate
- **Code evidence:** `mcp_server/benchmarks/eval_multihop.py`, `mcp_server/benchmarks/multihop_rrf.py`, `mcp_server/benchmarks/eval_retrieval.py`, `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `mcp_server/benchmarks/fixtures/eval_baseline.json`, `mcp_server/tests/test_multihop_rrf.py`, `mcp_server/tests/test_eval_multihop.py`, `docs/SEARCH_BEHAVIOR.md`, `docs/ARCHITECTURE.md`
- **Test debt:** No CI gate for eval_multihop; baseline snapshot not aligned to jina embed model; no unit test for compare_vs_baseline()
- **Verify:** —
- **Git:** pending
- **Changelog:** no

#### 2026-07-03 — plan
- **Phase / PR:** Phase 2 — automated 2-hop client eval script
- **Tracker status:** `planned`
- **Choices:** Separate `eval_multihop.py` (not extending `eval_retrieval.py` CLI); deterministic curated sub-questions in `golden_queries.jsonl` (no LLM in eval script); RRF fusion in `benchmarks/multihop_rrf.py` keyed by `chunk_id` with `rrf_k=60` from `Settings`; single PR; no CI gate change; GraphRAG / server-side hop fusion explicitly deferred to ADR 0002+ later phases. **Chosen scope:** Benchmark-only deliverable — `eval_multihop.py` + `multihop_rrf.fuse_hop_rrf`; curated `hop2_query_text` on four `multi_hop` golden entries; client-side RRF fusion of hop 1 (`query_text`) + hop 2 (`hop2_query_text`) via existing `run_search`; side-by-side ranx metrics vs single-pass on `multi_hop` slice; unit tests + opt-in benchmark smoke; `SEARCH_BEHAVIOR.md` + `ARCHITECTURE.md` command docs; optional `eval_baseline.json` `multi_hop_2hop` snapshot after live verify. No MCP server, compose, or runtime changes.
- **Assumptions:** Phase 2 = ADR follow-up "Automated 2-hop client script" (not tracker summary "server-side hop fusion"); indexed `codebase-indexer-mcp` collection available for manual verify; ADR 0007/0009 Phase 1 prerequisites satisfied; draft `hop2_query_text` values tunable during implementation
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** unit tests + opt-in benchmark smoke; live verify against indexed `codebase-indexer-mcp` collection for optional baseline JSON snapshot
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing no

#### 2026-07-03 — prioritization
- **Phase / PR:** Phase 2 — automated 2-hop client eval script
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0009 Phase 2 eval script over 0002 Phase 1 GraphRAG (tie on weighted score ~28 vs 27; lower scope/risk tie-breaker), 0014 Track A P2 outlier helper, 0008 test-debt closure, 0015 Phase 3 slim image, and 0014 Track B n8n; single phase per pipeline rule; no ADR Accept required; complete ADR 0009 validation before greenfield Neo4j or discovery P2. **Chosen scope:** Add deterministic 2-hop client eval script under `mcp_server/benchmarks/` (standalone `eval_multi_hop.py` or `eval_retrieval --multi-hop`): hop 1 `run_search` on original query; hop 2 sub-query derived deterministically from hop-1 results or golden fixture metadata; client-side RRF fuse on `chunk_id` (rrf_k=60); report `metrics_by_tag` for `multi_hop` slice; `--compare` against single-pass and `eval_baseline.json`; unit tests with mocked search; update `SEARCH_BEHAVIOR.md` Evaluation section; no server code or new services; defer server-side hop fusion and LLM-driven sub-questions in CI. **Why now:** ColBERT arc (0008 all phases, 0015 P1–P2) and vector discovery P1 (0014 `recommend_code`) are merged; ADR 0009 Phase 1 docs and four `multi_hop` golden queries shipped but Validation still requires automated 2-hop client script vs single-pass on `multi_hop` tag slice; `eval_retrieval.py` is single-pass only; no 2-hop benchmark module in repo; prerequisites (0007 harness, golden fixtures, SEARCH_BEHAVIOR guidance) satisfied; measurable without new infra; default deploy unchanged. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** —
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing unknown

#### 2026-07-02 — Phase 1 delivered
- **Phase / PR:** Phase 1 (docs + golden-set tags)
- **Choices:** Client-orchestrated decomposition; no new server code in phase 1
- **Code evidence:** `docs/SEARCH_BEHAVIOR.md`, `benchmarks/fixtures/golden_queries.jsonl` multi_hop tags
- **Changelog:** no (documentation-only phase)

---

### ADR 0014 — Vector discovery and ops automation

#### 2026-07-03 — merge
- **Phase / PR:** Track A — Phase 2 (outlier / diversity helper) — [PR #9](https://github.com/Tusquito/codebase-indexer-mcp/pull/9)
- **Tracker status:** `merged`
- **Choices:** squash merge `b97c29b` on feature branch `adr/0014-phase-2-outlier-helper`; ADR accepted as `Accepted (phase 1; phase 2 — outlier / diversity helper)`; release skipped; Track B (n8n compose) deferred
- **Deviations:** none
- **Code evidence:** merged via PR #9 (`adr/0014-phase-2-outlier-helper`; squash `b97c29b`; branch commits `5a691ab`, `7032668`, `22a9d76`)
- **Test debt:** carried from verification — scroll-supplement restriction unit test; `main.py` positive registration gate; combined `path_glob`+`context_chunk_ids` integration; live HTTP/Ollama e2e for `find_outlier_chunks`; golden-set outlier quality eval; multi-collection/sparse fusion deferred
- **Verify:** carried from verification — 287 unit tests passed; 17 targeted outlier tests passed; ruff clean; Docker integration report pass (8 pytest integration, smoke_recommend); review rounds: 1
- **Git:** [PR #9](https://github.com/Tusquito/codebase-indexer-mcp/pull/9) merged (squash `b97c29b`)
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — verification
- **Phase / PR:** Track A — Phase 2 (outlier / diversity helper)
- **Tracker status:** `verified`
- **Choices:** Separate tool `find_outlier_chunks`; `RecommendStrategy.BEST_SCORE` negative-only; cosine-to-centroid ascending sort + `OUTLIER_MAX_SIMILARITY` filter; reuse `RECOMMEND_ENABLED` (no `OUTLIER_ENABLED`); scroll supplement only when `path_glob` set or no explicit `context_chunk_ids`; `limit` cap 20; dense-only single-collection
- **Deviations:** Scroll supplement restricted when only `context_chunk_ids` provided — prevents outlier candidates being absorbed into context centroid during whole-collection scroll fill
- **Code evidence:** `mcp_server/src/codebase_indexer/tools/outliers.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/main.py`, `docker-compose.yml`, `.env.example`, `docs/SEARCH_BEHAVIOR.md`, `docs/ARCHITECTURE.md`, `README.md`, `mcp_server/tests/test_outliers.py`, `mcp_server/tests/test_outlier_tool.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_main.py`, `mcp_server/tests/test_storage_integration.py`
- **Test debt:** scroll-supplement restriction unit test; `main.py` positive registration gate; combined `path_glob`+`context_chunk_ids` integration; live HTTP/Ollama e2e for `find_outlier_chunks`; golden-set outlier quality eval; multi-collection/sparse fusion deferred
- **Verify:** tests run + plan compliance pass — 287 unit tests passed; 17 targeted outlier tests passed; ruff clean; Docker integration report pass (8 pytest integration, smoke_recommend); review rounds: 1
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Track A — Phase 2 (outlier / diversity helper)
- **Tracker status:** `implemented`
- **Choices:** Separate tool `find_outlier_chunks` (not extending `recommend_code`); score = cosine similarity to context centroid (ascending = most distant); reuse `RECOMMEND_ENABLED` gate (no `OUTLIER_ENABLED`); config `OUTLIER_MAX_CONTEXT_SAMPLES` (200) + `OUTLIER_MAX_SIMILARITY` (0.55); Qdrant `RecommendStrategy.BEST_SCORE` negative-only; scroll supplement only when `path_glob` set or no explicit `context_chunk_ids`
- **Deviations:** Scroll supplement restricted when only `context_chunk_ids` provided — prevents outlier candidates being absorbed into context centroid during whole-collection scroll fill
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/src/codebase_indexer/tools/outliers.py`, `mcp_server/src/codebase_indexer/main.py`, `docker-compose.yml`, `.env.example`, `docs/SEARCH_BEHAVIOR.md`, `docs/ARCHITECTURE.md`, `README.md`, `mcp_server/tests/test_outliers.py`, `mcp_server/tests/test_outlier_tool.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_main.py`, `mcp_server/tests/test_storage_integration.py`
- **Test debt:** `main.py` positive registration gate; live HTTP/Ollama e2e for `find_outlier_chunks`; combined `path_glob`+`context_chunk_ids` integration; golden-set outlier quality eval; multi-collection/sparse fusion deferred
- **Verify:** —
- **Git:** pending
- **Changelog:** no

#### 2026-07-03 — plan
- **Phase / PR:** Track A — Phase 2 (outlier / diversity helper)
- **Tracker status:** `planned`
- **Choices:** Lock tool name **`find_outlier_chunks`** (separate tool, do not extend `recommend_code`); lock score semantics to **cosine similarity to context centroid** (ascending sort = most distant first; `max_similarity` excludes above-threshold chunks); lock config to **reuse `RECOMMEND_ENABLED`** + add **`OUTLIER_MAX_CONTEXT_SAMPLES`** and **`OUTLIER_MAX_SIMILARITY`**; context from `context_chunk_ids` and/or scroll sample with optional `path_glob`; Qdrant retrieval via **`BEST_SCORE` negative-only** recommend (not `AVERAGE_VECTOR`); one PR for entire phase. **Chosen scope:** Add separate MCP tool `find_outlier_chunks` + `QdrantStorage.find_outlier_chunks` using Qdrant `RecommendStrategy.BEST_SCORE` negative-only recommend on sampled context vectors, cosine-to-centroid threshold filtering (`max_similarity` / `OUTLIER_MAX_SIMILARITY`), bounded `limit` (cap 20) + explicit required `collection`, dense-only single-collection; gate via existing `RECOMMEND_ENABLED` (no `OUTLIER_ENABLED`); new config `OUTLIER_MAX_CONTEXT_SAMPLES` (default 200); unit + integration tests per ADR Validation §Phase 2; `main.py` registration + `SEARCH_BEHAVIOR.md` + `ARCHITECTURE.md`/`README.md` sync; defer sparse fusion, multi-collection, Track B n8n compose, Discovery API context pairs
- **Assumptions:** Phase 1 `recommend_code` API frozen; whole-collection scan allowed when `path_glob` omitted (bounded by context sample cap); no new Python dependencies; `adr-finisher` updates ADR status after merge
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** unit + integration tests per ADR Validation §Phase 2; optional smoke script and compose harness step deferred
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — prioritization
- **Phase / PR:** Track A — Phase 2 (outlier / diversity helper)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0014 Track A P2 over Proposed 0002 GraphRAG Phase 1 (higher raw unlock but Accept gate + optional Neo4j greenfield — decision rules 2 & 5); over 0008 test-debt closure PR (closest QA alternative, same weighted tier ~20 but no user-facing capability); over 0009 eval_multihop CI gate (benchmark-only); over 0015 Phase 3 slim image (ops/build, deferred twice); over 0014 Track B n8n (ops-only, new optional service); single phase per pipeline rule; no ADR Accept required (0014 already Accepted phase 1); finish vector discovery Track A before GraphRAG or n8n. **Chosen scope:** Add outlier/diversity MCP discovery tool per ADR 0014 §Phase 2 — `find_outlier_chunks` (or `recommend_code` extension with `strategy=diverse` / score inversion, lock at plan); `QdrantStorage` helper; bounded `limit` + required explicit `collection`; dense-only single-collection; config gate if needed; unit + integration tests per ADR Validation §Phase 2; `main.py` registration + `SEARCH_BEHAVIOR.md`; defer sparse fusion, multi-collection, Track B n8n compose. **Why now:** ColBERT arc (0008 all phases), sidecar (0015 P1–P2), multi-hop eval (0009 P2), and recommendation search (0014 P1) are merged; ADR 0014 explicitly deferred Track A Phase 2 after P1; `recommend_code` and `QdrantStorage.recommend` exist in code but no outlier/diversity tool (`find_outlier_chunks` absent from source); prerequisites satisfied; user-facing discovery on existing embedder/Qdrant stack; no new mandatory infra; default deploy unchanged; completes Track A before ops-only n8n (Track B) or greenfield GraphRAG (0002, still Proposed). **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** —
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing unknown (likely yes)

#### 2026-07-03 — merge
- **Phase / PR:** Track A — Phase 1 (Recommendation search tool) — [PR #5](https://github.com/Tusquito/codebase-indexer-mcp/pull/5)
- **Tracker status:** `merged`
- **Choices:** merge on feature branch `adr/0014-phase-1-recommend-code`; ADR accepted as `Accepted (phase 1 — recommendation search tool)`; release skipped; Track A P2 (outlier helper) + Track B (n8n compose) deferred
- **Deviations:** none
- **Code evidence:** merged via PR #5 (`adr/0014-phase-1-recommend-code`)
- **Test debt:** carried from verification — `main.py` registration gate; live HTTP/Ollama e2e; golden-set eval; multi-collection deferred
- **Verify:** carried from verification — 258 pytest passed, ruff clean; review rounds: 2
- **Git:** [PR #5](https://github.com/Tusquito/codebase-indexer-mcp/pull/5) merged
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — verification
- **Phase / PR:** Track A — Phase 1 (Recommendation search tool)
- **Tracker status:** `verified`
- **Choices:** Tool name `recommend_code`; RecommendStrategy AVERAGE_VECTOR only; dense-only; path_glob post-filter fnmatch + limit×3; missing chunk IDs fail fast; multi-collection deferred
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/src/codebase_indexer/tools/recommend.py`, `mcp_server/src/codebase_indexer/main.py`, `docker-compose.yml`, `.env.example`, `docs/SEARCH_BEHAVIOR.md`, `mcp_server/tests/test_recommend.py`, `mcp_server/tests/test_recommend_tool.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_storage_integration.py`
- **Test debt:** `main.py` registration gate; live HTTP/Ollama e2e; golden-set eval; multi-collection deferred
- **Verify:** 258 pytest passed, ruff clean; review rounds: 2 (round 2 clean after R1 fix)
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Track A — Phase 1 (Recommendation search tool)
- **Tracker status:** `implemented`
- **Choices:** Tool name `recommend_code`; RecommendStrategy AVERAGE_VECTOR only; dense-only; path_glob post-filter fnmatch + limit×3; missing chunk IDs fail fast; multi-collection deferred
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/src/codebase_indexer/tools/recommend.py`, `mcp_server/src/codebase_indexer/main.py`, `docker-compose.yml`, `.env.example`, `docs/SEARCH_BEHAVIOR.md`, `mcp_server/tests/test_recommend.py`, `mcp_server/tests/test_recommend_tool.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_storage_integration.py`
- **Test debt:** `main.py` registration gate; live HTTP/Ollama e2e; golden-set eval; multi-collection deferred
- **Verify:** —
- **Git:** pending
- **Changelog:** no

#### 2026-07-03 — plan
- **Phase / PR:** Track A — Phase 1 (Recommendation search tool)
- **Tracker status:** `planned`
- **Choices:** Tool name `recommend_code`; RecommendStrategy AVERAGE_VECTOR only; dense-only; path_glob post-filter with fnmatch + limit*3 over-fetch; missing chunk IDs fail fast; multi-collection deferred; ADR Accept at merge via finisher. **Chosen scope:** `recommend_code` MCP tool + `QdrantStorage.recommend` helper + config (`RECOMMEND_ENABLED`, `RECOMMEND_MAX_EXAMPLES`) + unit/integration tests + `main.py` conditional registration + compose/.env.example + `SEARCH_BEHAVIOR.md` note; dense-only; single-collection; defer outlier helper (Track A P2), n8n compose (Track B), sparse fusion, multi-collection
- **Assumptions:** Qdrant v1.18.2 RecommendQuery API stable; existing payload indexes sufficient; no re-index required
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** unit/integration tests per chosen scope; `SEARCH_BEHAVIOR.md` recommend note
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — prioritization
- **Phase / PR:** Track A — Phase 1 (Recommendation search tool)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0014 Track A P1 over 0009 Phase 2 eval script (closest alternative, +1.5 weighted score but benchmark-only), 0008 Phase 2 track 2 adaptive rerank (incremental latency), Proposed 0002 GraphRAG Phase 1 (Neo4j greenfield), and 0015 Phase 3 slim image (deferred twice); single phase per pipeline rule; formal Accept of Proposed ADR required before dev. **Chosen scope:** `recommend_code` MCP tool + `QdrantStorage.recommend` helper + config (`RECOMMEND_ENABLED`, `RECOMMEND_MAX_EXAMPLES`) + unit/integration tests + `main.py` registration; dense-only; defer outlier helper (Track A P2), n8n compose (Track B), sparse fusion. **Why now:** ColBERT arc (0008 P1, 0015 P1–P2, 0008 P2 track 1) merged; open-decisions queue deferred Proposed 0002/0014 greenfield to this cycle; no recommend API in codebase; payload indexes already shipped; no new mandatory infra; user-facing discovery capability on existing embedder/Qdrant stack. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** —
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing unknown (likely yes)

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

### ADR 0016 — Adopt Qwen3-Embedding-4B as default Ollama dense model

#### 2026-07-03 — merge
- **Phase / PR:** Phase 1 — Config, Ollama MRL, docs, tests — [PR #12](https://github.com/Tusquito/codebase-indexer-mcp/pull/12)
- **Tracker status:** `merged`
- **Choices:** merge on feature branch `adr/0016-phase-1-qwen3-default`; ADR accepted as `Accepted (phase 1 — config, Ollama MRL, docs, tests)`; release skipped; Phase 2 eval baseline + `num_ctx` deferred
- **Deviations:** none
- **Code evidence:** merged via [PR #12](https://github.com/Tusquito/codebase-indexer-mcp/pull/12) (`adr/0016-phase-1-qwen3-default`)
- **Test debt:** carried from verification — Phase 2 eval baseline deferred
- **Verify:** carried from verification — 77 unit tests pass; integration 8/8 pass; plan compliance pass; review rounds: 1
- **Git:** [PR #12](https://github.com/Tusquito/codebase-indexer-mcp/pull/12) merged
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — verification
- **Phase / PR:** Phase 1 — Config, Ollama MRL, docs, tests
- **Tracker status:** `verified`
- **Choices:** Max tokens 32768; MRL 32≤size≤native; Qwen3 GPU defaults; compose generator Qwen3; ADR Accepted pre-merge
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/indexer/backends/ollama_dense.py`, `mcp_server/src/codebase_indexer/indexer/backends/factory.py`, `.env.example`, `scripts/run_compose_integration.py`, `mcp_server/benchmarks/_settings.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_ollama_dense_backend.py`, `mcp_server/tests/conftest.py`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md`, `README.md`, `docs/adr/0016-qwen3-embedding-default-dense-model.md`, `docs/adr/README.md`
- **Test debt:** Phase 2 eval baseline deferred
- **Verify:** 77 unit tests pass; integration 8/8 pass; plan compliance pass; review rounds: 1
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Phase 1 — Config, Ollama MRL, docs, tests
- **Tracker status:** `implemented`
- **Choices:** Max tokens 32768; MRL 32≤size≤native; Qwen3 GPU defaults; compose generator Qwen3; ADR Accepted pre-merge
- **Deviations:** `num_ctx` deferred; generator-only compose env
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/indexer/backends/ollama_dense.py`, `mcp_server/src/codebase_indexer/indexer/backends/factory.py`, `.env.example`, `scripts/run_compose_integration.py`, `mcp_server/benchmarks/_settings.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_ollama_dense_backend.py`, `mcp_server/tests/conftest.py`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md`, `README.md`, `docs/adr/0016-qwen3-embedding-default-dense-model.md`, `docs/adr/README.md`
- **Test debt:** Compose integration not smoke-run; Phase 2 eval baseline deferred
- **Verify:** —
- **Git:** pending
- **Changelog:** no

#### 2026-07-03 — plan
- **Phase / PR:** Phase 1 — Config, Ollama MRL, docs, tests
- **Tracker status:** `planned`
- **Choices:** Single PR Phase 1; ADR Accept pre-merge; compose integration generator updated to Qwen3 (`scripts/run_compose_integration.py`). **Chosen scope:** Qwen3 0.6B/4B/8B in `KNOWN_EMBED_MODEL_DIMENSIONS` + `KNOWN_EMBED_MODEL_MAX_TOKENS` with MRL-aware validation; `dimensions` passthrough in `OllamaDenseBackend` preload + `_embed_http`; update `.env.example`, `scripts/run_compose_integration.py`, `benchmarks/_settings.py`; unit tests; docs; defer Phase 2 eval baseline. **Assumptions:** 0017 P1 merged ([PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)); no new deps
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** —
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; status `planned` (not verified)

#### 2026-07-03 — prioritization
- **Phase / PR:** Phase 1 — Config, Ollama MRL, docs, tests
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0016 Phase 1 over 0002 Phase 2 GraphRAG payload linking (closest alternative, 32.5 weighted score — tie within ~10%; tie-breaker: default retrieval-path impact + embedding-track sequencing); over Proposed 0018 Phase 1 (ops observability, lower default-path impact); over 0017 Phase 2 (small slice; better combined with 0018 P1); over 0014 Track B n8n and 0015 Phase 3+ slim image (ops-only, deferred); over 0008 test-debt (QA-only); single phase per pipeline rule. **Chosen scope:** Qwen3 0.6B/4B/8B entries in `KNOWN_EMBED_MODEL_DIMENSIONS` and `KNOWN_EMBED_MODEL_MAX_TOKENS`; MRL `dimensions` passthrough in `ollama_dense.py` / `factory.py` when `DENSE_EMBED_VECTOR_SIZE` < native; update `.env.example`, `.env.compose.integration`, `benchmarks/_settings.py`; unit tests (`test_config.py`, `test_ollama_dense_backend.py` mock `dimensions` payload); docs (`ARCHITECTURE.md`, `DEPLOYMENT.md`, `README.md` embedding table — Qwen3 primary, Nomic CPU preset); defer Phase 2 `eval_baseline.json` refresh and operator re-index; **requires formal Accept of Proposed ADR 0016 before dev**. **Why now:** ADR 0017 Phase 1 merged ([PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)); prior 2026-07-03 prioritization deprioritized 0016 vs 0017 P1 and recommended sequential PR after 0017 P1 merge — prerequisite now satisfied. Code still defaults to Nomic (`DENSE_EMBED_MODEL=nomic-ai/nomic-embed-text-v1.5` in `.env.example`; no Qwen3 in `KNOWN_EMBED_MODEL_*`; `OllamaDenseBackend._embed_http` lacks MRL `dimensions`). Model-accurate truncation (0017 P1) enables trustworthy 32K caps for Qwen3. Golden-set eval harness exists for Phase 2; Phase 1 mergeable without baseline refresh. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** `.env.example` `DENSE_EMBED_MODEL=nomic-ai/nomic-embed-text-v1.5`; no Qwen3 in `KNOWN_EMBED_MODEL_*`; `OllamaDenseBackend._embed_http` lacks MRL `dimensions`
- **Test debt:** —
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing unknown

---

### ADR 0017 — Model-accurate tokenizer for Ollama dense truncation

#### 2026-07-03 — merge
- **Phase / PR:** Phase 1 — loader + Ollama backend — [PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)
- **Tracker status:** `merged`
- **Choices:** squash merge `a094bf5` on feature branch `adr/0017-phase-1-tokenizer-loader`; ADR accepted as `Accepted (phase 1 — loader + Ollama backend)` (docs commit `695b678`); release skipped; Phase 2 observability + ADR 0011 body edit deferred
- **Deviations:** none
- **Code evidence:** merged via PR #11 (`adr/0017-phase-1-tokenizer-loader`; squash `a094bf5`)
- **Test debt:** carried from verification — slow real-Nomic tokenizer test; no golden-set truncation accuracy fixture; Phase 2 metrics not implemented
- **Verify:** carried from verification — 22 unit tests pass; integration report pass (8 storage integration, compose deploy OK); plan compliance pass; review rounds: 1
- **Git:** [PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11) merged (squash `a094bf5`)
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — verification
- **Phase / PR:** Phase 1 — loader + Ollama backend
- **Tracker status:** `verified`
- **Choices:** tokenizers.Tokenizer.from_pretrained; HF env cache dirs; shared class-level tokenizer; fallback = log WARNING + pass text through unchanged; sparse BM25 untouched; Phase 2 observability + ADR 0011 edit deferred
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/indexer/tokenizer_loader.py`, `mcp_server/src/codebase_indexer/indexer/backends/ollama_dense.py`, `mcp_server/src/codebase_indexer/config.py`, `mcp_server/tests/test_ollama_dense_backend.py`, `mcp_server/tests/test_truncation.py`, `docs/ARCHITECTURE.md`, `.env.example`, `docker-compose.yml`
- **Test debt:** slow real-Nomic tokenizer test; no golden-set truncation accuracy fixture; Phase 2 metrics not implemented
- **Verify:** 22 unit tests pass; integration report pass (8 storage integration, compose deploy OK); plan compliance pass; review rounds: 1
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Phase 1 — loader + Ollama backend
- **Tracker status:** `implemented`
- **Choices:** Used `tokenizers.Tokenizer.from_pretrained`; cache dir from HF env vars; shared class-level tokenizer; fallback = log WARNING and pass text through unchanged; sparse BM25 path untouched; Phase 2 observability and ADR 0011 edit deferred
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/indexer/tokenizer_loader.py`, `mcp_server/src/codebase_indexer/indexer/backends/ollama_dense.py`, `mcp_server/src/codebase_indexer/config.py`, `docs/ARCHITECTURE.md`, `.env.example`, `docker-compose.yml`, `mcp_server/tests/test_ollama_dense_backend.py`, `mcp_server/tests/test_truncation.py`
- **Test debt:** Compose integration not smoke-run; slow real-nomic tokenizer test; no golden-set truncation accuracy fixture; Phase 2 metrics not implemented
- **Verify:** —
- **Git:** pending
- **Changelog:** no

#### 2026-07-03 — plan
- **Phase / PR:** Phase 1 — loader + Ollama backend (single PR)
- **Tracker status:** `planned`
- **Choices:** Single PR for Phase 1; mirror `OnnxSparseBackend` shared-tokenizer + `truncate_for_embedding` pattern; use `tokenizers.Tokenizer.from_pretrained` not `transformers.AutoTokenizer`; fallback = pass-through on load failure (log warning; not BM25, not char heuristic); no new mandatory infra; explicit `tokenizers` dep optional; ADR Accept before dev. **Chosen scope:** Add `indexer/tokenizer_loader.py` with `load_dense_tokenizer(model_id)` (HF Hub download + `HF_HOME`/`HF_HUB_CACHE`/`TRANSFORMERS_CACHE` resolution); class-level shared `tokenizers.Tokenizer` in `OllamaDenseBackend` loaded at `preload()` via `_ensure_truncation()`; replace `truncate_bm25_text` in `_truncate_batch` with `truncate_for_embedding`; fallback on load failure = log warning + pass text through unchanged (no BM25 fallback); unit tests with mock `Tokenizer` in `test_ollama_dense_backend.py` and loader/fallback in `test_truncation.py`; optional `@pytest.mark.slow` real Nomic tokenizer test; update `docs/ARCHITECTURE.md` and `.env.example` `HF_HOME` note; optional `docker-compose.yml` `HF_HOME` passthrough; defer Phase 2 observability and ADR 0011 body edit to finisher. **Assumptions:** `DENSE_EMBED_MODEL` is valid HF repo with tokenizer files; `tokenizers` remains transitive via fastembed; Phase 2 and ADR 0016 default switch are separate PRs; compose integration required for verification.
- **Deviations:** none
- **Code evidence:** —
- **Test debt:** optional `@pytest.mark.slow` real Nomic tokenizer test
- **Verify:** compose integration required for verification
- **Git:** pending
- **Changelog:** no — user-facing yes but status not yet verified

#### 2026-07-03 — prioritization
- **Phase / PR:** Phase 1 — loader + Ollama backend
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0017 Phase 1 over Proposed 0016 Phase 1 (closest alternative, −1.2 weighted score but higher scope/risk and breaking defaults); over 0002 Phase 2 GraphRAG payload linking (capability arc next but optional Neo4j + index payload work); over 0008 test-debt closure PR (QA-only, no capability); over 0009 eval_multihop CI gate (benchmark-only); over 0015 Phase 3 slim image and 0014 Track B n8n (ops-only, deferred twice); single phase per pipeline rule; embedding correctness before Qwen3 default switch; tie-breaker vs 0016: lower scope/risk. **Chosen scope:** Add `load_dense_tokenizer(model_id)` helper with HF Hub download + cache dir resolution; lazy-load shared `tokenizers.Tokenizer` in `OllamaDenseBackend` at preload; replace `truncate_bm25_text` in `_truncate_batch` with `truncate_for_embedding`; graceful fallback when tokenizer load fails (log warning; document behavior at plan); unit tests with mock `Tokenizer` in `test_ollama_dense_backend.py` and loader/fallback cases in `test_truncation.py`; optional `.env.example` `HF_HOME` note; update `docs/ARCHITECTURE.md` dense truncation behavior; defer Phase 2 observability (truncation metrics / token_count logs) and ADR 0011 body edit to finisher/plan; **requires formal Accept of Proposed ADR 0017 before dev**. **Why now:** Major arcs merged (0008 complete, 0015 P1–P2, 0014 Track A P1–P2, 0002 P1, 0009 P2); two new Proposed ADRs (0016/0017) form an embedding-quality track; code still uses BM25 word-split truncation in `OllamaDenseBackend._truncate_batch` (`truncate_bm25_text`) while `truncate_for_embedding`/`truncate_with_tokenizer` exist unused on the Ollama path; ADR 0016 Qwen3 default at 32K+ makes approximation errors material; 0017 Phase 1 is non-breaking (no re-index), satisfies ADR 0011 prerequisites, measurable via unit tests, no new mandatory infra; unlocks safe 0016 Phase 1 rollout next cycle. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** `OllamaDenseBackend._truncate_batch` uses `truncate_bm25_text`; `truncate_for_embedding`/`truncate_with_tokenizer` exist unused on Ollama path
- **Test debt:** —
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing unknown

---

### ADR 0018 — Adopt OpenTelemetry instrumentation with Prometheus metrics and optional OTLP export

#### 2026-07-03 — merge
- **Phase / PR:** Phase 1 — Application Prometheus metrics (MCP + ColBERT worker) — [PR #13](https://github.com/Tusquito/codebase-indexer-mcp/pull/13)
- **Tracker status:** `merged`
- **Choices:** merge on feature branch `adr/0018-phase-1-prometheus-metrics`; ADR accepted as `Accepted (phase 1 — Application Prometheus metrics (MCP + ColBERT worker))`; release skipped; Phase 2 OTel traces, Phase 3 observability compose stack deferred
- **Deviations:** none
- **Code evidence:** merged via [PR #13](https://github.com/Tusquito/codebase-indexer-mcp/pull/13) (`adr/0018-phase-1-prometheus-metrics`; `516b5feee19a81214b47dfaf135fa46391021a9b`)
- **Test debt:** carried from verification — Bearer-auth /metrics test; truncated_chunks helper test; in-process ColBERT embed metrics; memory pressure edge-trigger
- **Verify:** carried from verification — 329 tests pass; plan compliance pass; review rounds: 1
- **Git:** [PR #13](https://github.com/Tusquito/codebase-indexer-mcp/pull/13) merged (`516b5feee19a81214b47dfaf135fa46391021a9b`)
- **Changelog:** no — already in `[Unreleased]` from verified step

#### 2026-07-03 — verification
- **Phase / PR:** Phase 1 — Application Prometheus metrics (MCP + ColBERT worker)
- **Tracker status:** `verified`
- **Choices:** Dedicated CollectorRegistry; ColBERT ONNX at worker HTTP layer; index metrics via IndexJobTracker; Docker skip per plan
- **Deviations:** none
- **Code evidence:** carried from implementation — `telemetry/metrics.py`, `main.py`, `colbert_worker/app.py`, `tools/*.py`, `search_common.py`, backends, `memory.py`, `DEPLOYMENT.md`, `test_telemetry_metrics.py`
- **Test debt:** Bearer-auth /metrics test; truncated_chunks helper test; in-process ColBERT embed metrics; memory pressure edge-trigger
- **Verify:** tests run + plan compliance pass (329 passed); review rounds: 1
- **Git:** pending
- **Changelog:** yes

#### 2026-07-03 — implementation
- **Phase / PR:** Phase 1 — Application Prometheus metrics (MCP + ColBERT worker)
- **Tracker status:** `implemented`
- **Choices:** Opt-in `METRICS_ENABLED=false` default; `prometheus_client` on dedicated registry; metrics-only `@observe_tool`; no collection/rel_path labels; `METRICS_PORT` and docker-compose deferred
- **Deviations:** Dedicated `CollectorRegistry`; pytest-asyncio re-added to dev deps; colbert_onnx metrics at worker HTTP layer only
- **Code evidence:** `telemetry/metrics.py`, `main.py`, `colbert_worker/app.py`, `tools/*.py`, `search_common.py`, backends, `memory.py`, `DEPLOYMENT.md`, `test_telemetry_metrics.py`
- **Test debt:** Bearer-auth /metrics integration; compose scrape smoke; Phase 2 OTel span tests
- **Verify:** —
- **Git:** pending
- **Changelog:** no

#### 2026-07-03 — plan
- **Phase / PR:** Phase 1 — Application Prometheus metrics (MCP + ColBERT worker)
- **Tracker status:** `planned`
- **Choices:** Single PR per phase; metrics-only `@observe_tool` decorator on all MCP tool handlers (not custom OTel spans); `prometheus_client>=0.21` in main dependencies with `METRICS_ENABLED=false` runtime gate; truncation counter wired; Qdrant scrape documented only in `DEPLOYMENT.md`; Docker compose unchanged in Phase 1; defer Phase 2 OTel traces, Phase 3 observability compose stack. **Chosen scope:** Accept ADR 0018 then implement Phase 1 only: `telemetry/metrics.py` with `METRICS_ENABLED=false` default; thin metrics-only decorator; application counters/histograms; `GET /metrics` on MCP and ColBERT worker; unit tests; `DEPLOYMENT.md` scrape docs. **Assumptions:** ADR Accept at finisher after merge; default CI metrics-disabled.
- **Deviations:** none
- **Code evidence:** zero application `/metrics` endpoint or Prometheus instrumentation in codebase today
- **Test debt:** —
- **Verify:** —
- **Git:** pending
- **Changelog:** no — user-facing yes; status `planned` (changelog at verified)

#### 2026-07-03 — prioritization
- **Phase / PR:** Phase 1 — Application Prometheus metrics (MCP + ColBERT worker)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0018 Phase 1 over 0016 Phase 2 eval baseline; single phase per pipeline rule. **Chosen scope:** Accept ADR 0018 then implement Phase 1 only: `telemetry/metrics.py` with `METRICS_ENABLED=false` default; `prometheus_client>=0.21`; thin metrics-only decorator on MCP tool handlers; counters/histograms; `GET /metrics` on MCP and ColBERT worker; unit tests; `DEPLOYMENT.md` Qdrant scrape docs; defer Phase 2 OTel traces, Phase 3 compose stack; **requires formal Accept of Proposed ADR 0018 before dev**. **Why now:** Embedding prerequisites merged (0016 P1 [PR #12](https://github.com/Tusquito/codebase-indexer-mcp/pull/12), 0017 P1 [PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)); multi-container topology from 0015 makes cross-service latency/OOM the dominant ops gap; zero instrumentation in code; 0017 Phase 2 explicitly deferred to 0018 metric namespace. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** zero application `/metrics` endpoint or Prometheus instrumentation in codebase today
- **Test debt:** —
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
| 3.5 | Docker integration (compose deploy + live tests) | — | no |
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
3.5. **Docker integration** — `adr-integration-tester` deploys Compose stack, runs live Qdrant pytest + MCP health; required when plan touches deploy/runtime (see plan **Docker integration**).
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
| 2026-07-03 | 0008 | Order of remaining Phase 2 tracks (adaptive skip vs per-tool override) | Track 2a (adaptive skip) prioritized over track 2b (per-tool override) at 2026-07-03 prioritization; track 2a merged ([PR #6](https://github.com/Tusquito/codebase-indexer-mcp/pull/6)); track 2b now `planned` | no |
| 2026-07-03 | 0008 | Confirm track 2a scope before track 2b | Track 2a scope confirmed at plan — adaptive skip in `_search_single`; track 2b per-tool override deferred | no |
| 2026-07-03 | 0008 | Adaptive skip implementation location | Hybrid RRF probe in `QdrantStorage._search_single` before ColBERT; `AdaptiveRerankStats` on storage | no |
| 2026-07-03 | 0008 | New env vars for adaptive rerank | Shipped `RERANK_ADAPTIVE_ENABLED=true`, `RERANK_ADAPTIVE_GAP=0.02` | no |
| 2026-07-03 | 0008 | ColBERT query embed path in track 2a | Unchanged — `Embedder.embed_query` not modified; skip decision only | no |
| 2026-07-03 | 0008 | Multi-collection adaptive gap measurement | Per-collection in `_search_single`; then existing `fuse_cross_collection_rrf` | no |
| 2026-07-03 | 0008 | Final `RERANK_ADAPTIVE_GAP` default after golden-set sweep | Shipped `0.02`; golden-set sweep via `eval_retrieval --rerank` still open for tuning validation | no |
| 2026-07-03 | 0008 | Confirm `RERANK_ADAPTIVE_ENABLED` default for operators on `RERANK_ENABLED=true` | Shipped `true` | no |
| 2026-07-03 | 0008 | Adaptive probe limit for gap measurement | `max(top_k, 2)` | no |
| 2026-07-03 | 0008 | Adaptive skip when probe returns fewer than 2 hits | Always run ColBERT (no skip) | no |
| 2026-07-03 | 0008 | Live Qdrant integration test vs unit mocks only for adaptive skip | Open — test debt at verification | no |
| 2026-07-03 | 0008 | Multi-collection adaptive skip + global RRF unit test | Open — deferred to verification | no |
| 2026-07-03 | 0008 | Dedicated unit test for single-probe-hit ColBERT path (< 2 probe hits) | Open — test debt at verification | no |
| 2026-07-03 | 0008 | Golden-set gap threshold sweep for `RERANK_ADAPTIVE_GAP` tuning | Open — test debt at verification (`eval_retrieval --rerank`) | no |
| 2026-07-03 | 0009 | Whether 0009 Phase 2 eval script runs parallel or next cycle | Prioritized 2026-07-03 — Phase 2 automated 2-hop client eval script is `planned` | no |
| 2026-07-03 | 0008 | Accept Proposed 0002 or 0014 in a subsequent cycle for greenfield work? | 0014 Track A complete (P1+P2 merged); 0002 Phase 1 merged ([PR #10](https://github.com/Tusquito/codebase-indexer-mcp/pull/10)) | no |
| 2026-07-03 | 0002 | Accept ADR 0002 (Proposed → Accepted) before dev? | `Accepted (phase 1 — Neo4j storage + index-time graph writer)` after PR #10 merge (docs commit `a48dd97`) | no |
| 2026-07-03 | 0002 | ADR index wording after Phase 1 merge | `Accepted (phase 1 — Neo4j storage + index-time graph writer)` after PR #10 merge (docs commit `a48dd97`) | no |
| 2026-07-03 | 0002 | Accept ADR 0002 phase 1 at merge? | `Accepted (phase 1 — Neo4j storage + index-time graph writer)` after PR #10 merge | no |
| 2026-07-03 | 0002 | Testcontainers Neo4j vs bolt mock for CI | Decided at implementation — mock driver CI default | no |
| 2026-07-03 | 0002 | Graph write fail-fast vs best-effort | Decided at plan — best-effort: graph write errors append to `PipelineResult.errors` while Qdrant upsert succeeds | no |
| 2026-07-03 | 0002 | Endpoint `method` inference depth | Decided at implementation — best-effort only | no |
| 2026-07-03 | 0002 | Neo4j Python driver version | Shipped `neo4j` 6.2.0 (ADR/plan assumed 5.x) | no |
| 2026-07-03 | 0002 | Neo4j in base compose vs override only | Decided at plan — compose override only (`docker-compose.neo4j.yml`); not base `docker-compose.yml` | no |
| 2026-07-03 | 0002 | Promote `_extract_imported_names` to public chunker API | Decided at plan — yes; public `extract_imported_names` | no |
| 2026-07-03 | 0002 | Manifest `BUILD_DEPENDS` source for graph writer | Decided at plan — on-disk re-read for full file content | no |
| 2026-07-03 | 0002 | New MCP tools in Phase 1? | Decided at plan — no; index-time graph writer only | no |
| 2026-07-03 | 0002 | Full re-index when enabling graph on existing collections | Decided at plan — yes; document in `.env.example` + `ARCHITECTURE.md` | no |
| 2026-07-03 | 0002 | Whether to run 0009 CI gate or 0008 test-debt PR in parallel with Accept/plan | Open — orchestrator decision | no |
| 2026-07-03 | 0014 | Accept ADR 0014? | `Accepted (phase 1 — recommendation search tool)` after PR #5 merge | no |
| 2026-07-03 | 0014 | Lock tool name/schema | Tool name `recommend_code`; RecommendStrategy AVERAGE_VECTOR only | no |
| 2026-07-03 | 0014 | Confirm dense-only Phase 1 | Dense-only confirmed; sparse fusion deferred | no |
| 2026-07-03 | 0014 | path_glob post-filter strategy | fnmatch post-filter with limit*3 over-fetch | no |
| 2026-07-03 | 0014 | Missing positive chunk IDs | Fail fast | no |
| 2026-07-03 | 0014 | Multi-collection recommend | Deferred; single-collection Phase 1 | no |
| 2026-07-03 | 0014 | Whether to run 0009 Phase 2 eval script as parallel lightweight PR | Closed at 0009 prioritization — 0009 Phase 2 chosen as primary cycle work; parallel vs sequential with 0008 test-debt PR still open | no |
| 2026-07-03 | 0009 | Deterministic sub-query generation strategy for CI | Decided at plan — curated `hop2_query_text` inline in `golden_queries.jsonl`; no LLM in eval script | no |
| 2026-07-03 | 0009 | Script shape: new `eval_multi_hop.py` vs `--multi-hop` flag on `eval_retrieval` | Decided at plan — separate `eval_multihop.py` (not extending `eval_retrieval.py` CLI) | no |
| 2026-07-03 | 0009 | Target `multi_hop` recall improvement threshold vs baseline 0.5 | Open — minimum recall lift threshold for `eval_baseline.json` `multi_hop_2hop` snapshot commit (plan or verification) | no |
| 2026-07-03 | 0009 | `hop2_query_text` storage: inline in golden fixture vs separate file | Decided at implementation — inline in `golden_queries.jsonl` | no |
| 2026-07-03 | 0009 | Ship `--rerank` passthrough on `eval_multihop.py` in Phase 2? | Decided at implementation — yes, included | no |
| 2026-07-03 | 0009 | ADR index wording after Phase 2 merge | `Accepted (phase 1; phase 2 merged)` after PR #8 merge (commit `d761d09` on main) | no |
| 2026-07-03 | 0009 | Accept ADR 0009 phase 2 at merge? | `Accepted (phase 1; phase 2 merged)` after PR #8 merge | no |
| 2026-07-03 | 0009 | `eval_baseline.json` `multi_hop_2hop` embed model alignment | Live snapshot used local nomic embed model (not baseline jina model); re-align at verification or merge | no |
| 2026-07-03 | 0009 | CI gate for `eval_multihop` | Open — no CI gate at implementation; test debt | no |
| 2026-07-03 | 0009 | Unit test for `compare_vs_baseline()` | Open — test debt at implementation | no |
| 2026-07-03 | 0009 | Client-side RRF fusion module location | Decided at plan — `benchmarks/multihop_rrf.py` with `fuse_hop_rrf`; `rrf_k=60` from `Settings` | no |
| 2026-07-03 | 0009 | Server-side hop fusion / GraphRAG in Phase 2? | Decided at plan — explicitly deferred to ADR 0002+ later phases | no |
| 2026-07-03 | 0008 | Whether to run 0008 test-debt closure PR in parallel with 0009 Phase 2 | Open — orchestrator decision | no |
| 2026-07-03 | 0008 | Whether `rerank=false` skips ColBERT query embed in `Embedder.embed_query` | Decided at plan — yes; skip via `colbert_vector=None` in `Embedder.embed_query` / `embed_queries` when tool `rerank=false` | no |
| 2026-07-03 | 0008 | Confirm `recommend_code` excluded from per-tool `rerank` parameter | Decided at plan — excluded; not in track 2b scope (`search_codebase`, `search_symbols`, xref/service_map semantic paths only) | no |
| 2026-07-03 | 0008 | Default `None` for per-tool `rerank` preserves global `RERANK_ENABLED` behavior | Decided at plan — `rerank=None` preserves current behavior | no |
| 2026-07-03 | 0008 | Per-tool override implementation layer | Decided at plan — embed + tool layer (not new storage flag); `rerank=false` only effective when `RERANK_ENABLED=true` | no |
| 2026-07-03 | 0008 | Adaptive skip interaction when per-tool `rerank` set | Decided at plan — track 2a adaptive skip unchanged when effective rerank is on | no |
| 2026-07-03 | 0008 | Whether import-phrased xref search inherits tool-level `rerank` | Decided at implementation — yes; import-phrased xref search inherits tool-level `rerank` | no |
| 2026-07-03 | 0008 | Whether `rerank=true` should bypass adaptive skip | Decided at implementation — no; `rerank=true` does not enable ColBERT without global flag or bypass adaptive skip | no |
| 2026-07-03 | 0008 | Per-tool rerank embed gate expression | `use_rerank = self.rerank and rerank is not False` in `Embedder.embed_query` / `embed_queries` | no |
| 2026-07-03 | 0008 | Payload-only xref paths and per-tool `rerank` | Exact symbol / call_site paths unaffected by tool-level `rerank` | no |
| 2026-07-03 | 0008 | Optional `[Unreleased]` changelog bullet for opt-in rerank deployments | Closed at verified — per-tool `rerank=false` bullet added to `[Unreleased]` | no |
| 2026-07-03 | 0008 | `colbert_vector=None` interaction with storage rerank/adaptive paths | Verified — `colbert_vector=None` skips storage rerank and adaptive skip paths | no |
| 2026-07-03 | 0008 | ADR 0008 phase completion at track 2b | Track 2b merged ([PR #7](https://github.com/Tusquito/codebase-indexer-mcp/pull/7)); ADR 0008 full **Accepted**; ColBERT arc complete | no |
| 2026-07-03 | 0008 | Accept ADR 0008 full Accepted (phase 2 complete)? | **Accepted** after PR #7 merge; phase 1 + phase 2 tracks 1, 2a, 2b all merged | no |
| 2026-07-03 | 0014 | Lock tool name/API (`find_outlier_chunks` vs `recommend_code` strategy param) | Decided at plan — separate tool `find_outlier_chunks`; do not extend `recommend_code` | no |
| 2026-07-03 | 0014 | Similarity threshold / score inversion semantics | Decided at plan — cosine similarity to context centroid; ascending sort = most distant first; `max_similarity` excludes above-threshold chunks | no |
| 2026-07-03 | 0014 | Whether to add `OUTLIER_ENABLED` config or reuse `RECOMMEND_ENABLED` | Decided at plan — reuse `RECOMMEND_ENABLED`; add `OUTLIER_MAX_CONTEXT_SAMPLES` (default 200) and `OUTLIER_MAX_SIMILARITY` | no |
| 2026-07-03 | 0014 | Qdrant recommend strategy for outlier helper | Decided at plan — `RecommendStrategy.BEST_SCORE` negative-only (not `AVERAGE_VECTOR`) | no |
| 2026-07-03 | 0014 | Context source for outlier helper | Decided at implementation — scroll supplement only when `path_glob` set or no explicit `context_chunk_ids`; restricted when only `context_chunk_ids` provided (prevents centroid pollution) | no |
| 2026-07-03 | 0014 | Sparse fusion and multi-collection for outlier helper | Deferred — same as Phase 1 | no |
| 2026-07-03 | 0014 | Discovery API context pairs | Deferred — out of Phase 2 scope | no |
| 2026-07-03 | 0014 | Optional smoke script and compose harness step | Deferred | no |
| 2026-07-03 | 0014 | `max_similarity` default value | Decided at implementation — shipped `OUTLIER_MAX_SIMILARITY=0.55`; golden-set tuning validation still open | no |
| 2026-07-03 | 0014 | Parallel vs sequential with 0008 test-debt PR | Open — orchestrator decision | no |
| 2026-07-03 | 0014 | Scroll supplement when only `context_chunk_ids` provided | Restricted at implementation — no whole-collection scroll fill; prevents outlier candidates polluting context centroid; verified at 2026-07-03 verification | no |
| 2026-07-03 | 0014 | `OUTLIER_MAX_SIMILARITY` default after golden-set tuning | Shipped `0.55`; golden-set outlier quality eval still open — test debt at verification | no |
| 2026-07-03 | 0014 | Accept ADR 0014 phase 2 at merge? | `Accepted (phase 1; phase 2 — outlier / diversity helper)` after PR #9 merge | no |
| 2026-07-03 | 0014 | ADR index wording after Phase 2 merge | `Accepted (phase 1; phase 2 — outlier / diversity helper)` after PR #9 merge | no |
| 2026-07-03 | 0014 | Track A completion at Phase 2 merge | Track A Phase 1 + Phase 2 merged ([PR #5](https://github.com/Tusquito/codebase-indexer-mcp/pull/5), [PR #9](https://github.com/Tusquito/codebase-indexer-mcp/pull/9)); Track B n8n compose remains deferred | no |
| 2026-07-03 | 0017 | Accept ADR 0017 (Proposed → Accepted) before dev? | **Accepted (phase 1 — loader + Ollama backend)** after PR #11 merge (docs commit `695b678`) | no |
| 2026-07-03 | 0017 | Accept ADR 0017 phase 1 at merge? | `Accepted (phase 1 — loader + Ollama backend)` after PR #11 merge | no |
| 2026-07-03 | 0017 | ADR index wording after Phase 1 merge | `Accepted (phase 1 — loader + Ollama backend)` after PR #11 merge | no |
| 2026-07-03 | 0017 | Phase 1 implementation choices confirmed | `tokenizers.Tokenizer.from_pretrained`; HF env cache dirs; shared class-level tokenizer; fallback WARNING + pass-through; sparse BM25 untouched; Phase 2 observability + ADR 0011 edit deferred | no |
| 2026-07-03 | 0017 | Tokenizer load-failure fallback behavior | Decided at plan — log warning + pass text through unchanged (no BM25 fallback, not char heuristic) | no |
| 2026-07-03 | 0017 | 0016 Phase 1 sequencing after 0017 P1 | Prioritized 0017 P1 over 0016 P1 at 2026-07-03; 0017 P1 merged ([PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)); 0016 Phase 1 unblocked for next cycle | no |
| 2026-07-03 | 0017 | Air-gap HF cache pre-seeding policy for operators | Decided at plan — document only in Phase 1 (pre-populate `HF_HOME` or mount tokenizer files; no implementation) | no |
| 2026-07-03 | 0016 | Whether 0016 Phase 1 runs this cycle | **Prioritized** at 2026-07-03 prioritization — 0017 P1 merged ([PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)); prerequisite satisfied; tracker `planned` at 2026-07-03 plan | no |
| 2026-07-03 | 0016 | Accept ADR 0016 (Proposed → Accepted) before dev? | **Accepted (phase 1 — config, Ollama MRL, docs, tests)** at 2026-07-03 implementation (pre-merge) | no |
| 2026-07-03 | 0016 | Single PR vs split Phase 1 | Decided at plan — single PR Phase 1 | no |
| 2026-07-03 | 0016 | Compose integration model preset | Decided at plan — update `scripts/run_compose_integration.py` generator to Qwen3 | no |
| 2026-07-03 | 0016 | MRL `dimensions` passthrough location | Decided at plan — `OllamaDenseBackend` preload + `_embed_http` (not `factory.py`) | no |
| 2026-07-03 | 0016 | New dependencies for Phase 1 | Assumed at plan — no new deps | no |
| 2026-07-03 | 0016 | `.env.example` default: Qwen3 GPU preset vs Nomic-with-Qwen3-documented | Decided at implementation — Qwen3 GPU defaults in `.env.example`; Nomic documented as CPU/low-VRAM preset | no |
| 2026-07-03 | 0016 | Whether 0002 Phase 2 supersedes if GraphRAG adoption is active | Open — orchestrator decision | no |
| 2026-07-03 | 0016 | Phase 1 implementation choices confirmed | Max tokens 32768; MRL 32≤size≤native; Qwen3 GPU defaults in `.env.example`; compose generator Qwen3; ADR Accepted pre-merge; `num_ctx` deferred; generator-only compose env (no `.env.compose.integration` file) | no |
| 2026-07-03 | 0016 | Phase 2 recall@10 gate strictness | Open — plan or verification decision | no |
| 2026-07-03 | 0018 | Accept ADR 0018 (Proposed → Accepted) before dev? | **Accepted (phase 1 — Application Prometheus metrics (MCP + ColBERT worker))** after PR #13 merge | no |
| 2026-07-03 | 0018 | Whether 0017 P2 truncation logging ships in same PR or after 0018 P1 merge? | **Resolved** at 2026-07-03 plan — deferred until after 0018 P1 merge (orchestrator resolved) | no |
| 2026-07-03 | 0018 | Single PR per phase for 0018? | Decided at plan — yes; one PR for Phase 1 | no |
| 2026-07-03 | 0018 | MCP tool instrumentation approach | Decided at plan — metrics-only `@observe_tool` decorator on all MCP tool handlers; defer custom OTel spans to Phase 2 | no |
| 2026-07-03 | 0018 | `prometheus_client` dependency placement | Decided at plan — main dependencies with `METRICS_ENABLED=false` runtime gate | no |
| 2026-07-03 | 0018 | Truncation counter in Phase 1 | Decided at plan — wired in Phase 1 | no |
| 2026-07-03 | 0018 | Qdrant metrics in Phase 1 | Decided at plan — scrape documented only in `DEPLOYMENT.md`; no Qdrant code changes | no |
| 2026-07-03 | 0018 | Docker compose changes in Phase 1 | Decided at plan — unchanged in Phase 1 | no |
| 2026-07-03 | 0018 | Default CI metrics posture | Assumed at plan — metrics-disabled (`METRICS_ENABLED=false`) | no |
| 2026-07-03 | 0018 | Prioritize 0018 Phase 1 over 0016 Phase 2 eval baseline? | **Prioritized** at 2026-07-03 prioritization — 0016 P1 + 0017 P1 merged; 0018 P1 `planned` at 2026-07-03 plan | no |
| 2026-07-03 | 0016 | Whether 0016 Phase 2 runs this cycle | Deprioritized at 2026-07-03 — 0018 Phase 1 prioritized over 0016 Phase 2 eval baseline | no |
| 2026-07-03 | 0018 | Dedicated CollectorRegistry for Prometheus metrics | Decided at implementation — dedicated `CollectorRegistry` instead of default registry | no |
| 2026-07-03 | 0018 | collection/rel_path metric labels | Decided at implementation — omitted (no collection/rel_path labels) | no |
| 2026-07-03 | 0018 | METRICS_PORT env var and docker-compose scrape wiring | Deferred at implementation — `METRICS_PORT` and docker-compose unchanged | no |
| 2026-07-03 | 0018 | colbert_onnx backend metrics instrumentation | Decided at implementation — metrics at ColBERT worker HTTP layer only (not in-process onnx backend) | no |
| 2026-07-03 | 0018 | pytest-asyncio in dev dependencies | Decided at implementation — re-added to dev deps | no |
| 2026-07-03 | 0018 | Phase 1 implementation choices confirmed | Opt-in `METRICS_ENABLED=false`; dedicated registry; metrics-only `@observe_tool`; no collection/rel_path labels; `METRICS_PORT` + compose deferred | no |
| 2026-07-03 | 0018 | Index metrics instrumentation location | Decided at verification — index metrics via IndexJobTracker | no |
| 2026-07-03 | 0018 | Phase 1 verification confirmed | Dedicated CollectorRegistry; ColBERT ONNX at worker HTTP layer; index metrics via IndexJobTracker; Docker skip per plan; 329 tests pass; test debt: Bearer-auth /metrics, truncated_chunks helper, in-process ColBERT embed metrics, memory pressure edge-trigger | no |
| 2026-07-03 | 0018 | Accept ADR 0018 phase 1 at merge? | `Accepted (phase 1 — Application Prometheus metrics (MCP + ColBERT worker))` after PR #13 merge | no |
| 2026-07-03 | 0018 | Phase 1 merge confirmed | [PR #13](https://github.com/Tusquito/codebase-indexer-mcp/pull/13) merged on `adr/0018-phase-1-prometheus-metrics`; release skipped; Phase 2 OTel traces + Phase 3 compose stack deferred | no |
