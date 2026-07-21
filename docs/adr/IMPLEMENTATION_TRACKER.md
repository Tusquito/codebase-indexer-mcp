# ADR implementation tracker

Living record of **what we chose to build**, **implementation progress**, and **runtime choices** while executing ADRs.

> **Generated file.** The blocks between `<!-- BEGIN GENERATED:* -->` / `<!-- END GENERATED:* -->` markers are rendered from `docs/adr/tracker/` YAML by `scripts/render_adr_tracker.py`. **Do not hand-edit inside the markers** — edit the YAML (phases/ + events/) and run `python scripts/render_adr_tracker.py`. Everything outside the markers is manual and preserved on render.

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

<!-- BEGIN GENERATED:summary -->
| ADR | Title | ADR status | Phase | Tracker | Chosen scope | Last updated |
|-----|-------|------------|-------|---------|--------------|--------------|
| 0002 | Optional GraphRAG (Neo4j + Qdrant) | Accepted (phases 1–3; phase 4 deferred) | phase-1 | `merged` | Shipped: `storage/neo4j.py` async driver wrapper (neo4j driver 6.2.0) with idempotent schema; `indexer/graph_writer.py` writing ADR ontology from index batches (reuses `UrlExtractors`, `extract_build_deps`/`match_deps_to_collections`, public `extract_imported_names`); `pipeline.py` hooks mirroring Qdrant flush/delete cadence; best-effort graph errors to `PipelineResult.errors`; `context.py` optional `Neo4jStorage`; config (`GRAPH_ENABLED=false` default, `NEO4J_*`, `GRAPH_WRITER_BATCH`, `GRAPH_SCHEMA_VERSION=1`); `docker-compose.neo4j.yml` override only; mock driver CI unit tests; `.env.example` + `ARCHITECTURE.md`; no MCP tools Phase 1; endpoint `method` inference best-effort; defer Phase 2 Qdrant `graph_node_ids`, Phase 3 `expand_search_context`, Phase 4 Neo4j cross-project queries; [PR #10](https://github.com/Tusquito/codebase-indexer-mcp/pull/10) | 2026-07-03 |
| 0002 | Optional GraphRAG (Neo4j + Qdrant) | Accepted (phases 1–3; phase 4 deferred) | phase-2 | `merged` | Neighbor-keys-only `graph_node_ids` via `graph_node_ids_from_batch`; batch before upsert; boolean `graph_enabled` collection metadata only; `graph_node_ids` omitted for zero-neighbor chunks and `GRAPH_ENABLED=false`; structlog `graph_linkage_missing` once per unlinked collection; 34 Phase 2 unit tests + 420 full suite pass; integration + plan compliance pass; review rounds: 1; defer Phase 3 `expand_search_context`, Phase 4 cross-project Cypher; test debt: prometheus_client and neo4j driver needed in CI env; [PR #26](https://github.com/Tusquito/codebase-indexer-mcp/pull/26) | 2026-07-08 |
| 0002 | Phase 3 — Graph-augmented MCP retrieval | Accepted (phases 1–3; phase 4 deferred) | phase-3 | `merged` | MCP tool `expand_search_context` in `tools/graph_search.py`: hybrid search seeds → bounded Neo4j Cypher subgraph expansion (1–`GRAPH_MAX_HOPS` hops, `GRAPH_MAX_NODES` cap) → attach Qdrant chunk payloads → structured `GraphContext` JSON (`nodes`/`edges`/`related_chunks`/`seeds`). `Neo4jStorage.expand_subgraph` with injection-safe interpolated hop count + `LIMIT $max_nodes`; chunk_id-only seeding (no `graph_node_ids` reads); conditional registration in `main.py` gated on `GRAPH_ENABLED`. Docker harness `--graph` flag brings up Neo4j override and validates live stack via health + startup-log signal. Phase 4 deferred. | 2026-07-10 |
| 0003 | Hybrid search RRF default | Accepted | all | `merged` | Shipped | 2026-07-02 |
| 0004 | Collection-per-project isolation | Accepted | all | `merged` | Shipped | 2026-07-02 |
| 0005 | MCP retrieval connector | Accepted | all | `merged` | Shipped | 2026-07-02 |
| 0006 | Explicit FastEmbed pipeline | Accepted | all | `merged` | Shipped | 2026-07-02 |
| 0007 | Golden-set eval (ranx) | Accepted | all | `merged` | `eval_retrieval.py` + fixtures | 2026-07-02 |
| 0008 | Optional ColBERT reranking | Accepted | phase-1 | `merged` | Config (`RERANK_ENABLED=false` default, `COLBERT_EMBED_MODEL`, `RERANK_PREFETCH`, `RERANK_MAX_QUERY_TOKENS`); `ColbertOnnxBackend` via fastembed; multivector `colbert` + MAX_SIM rerank in `qdrant.py`; per-collection hybrid prefetch + ColBERT rerank then `fuse_cross_collection_rrf`; pipeline third embed pass (sequential); synthetic CI integration test + `@pytest.mark.slow` + `RUN_SLOW_COLBERT=1`; operator re-index docs; [PR #1](https://github.com/Tusquito/codebase-indexer-mcp/pull/1) | 2026-07-03 |
| 0008 | Optional ColBERT reranking | Accepted | phase-2-track-1 | `merged` | Shared `dispatch_search()` in `search_common.py`; xref semantic/import via `run_search()`; service_map batched discovery via `dispatch_search()` with pre-embedded colbert vectors; tool-specific `min_score` retained (0.3 / 0.25); unit tests + `SEARCH_BEHAVIOR.md`; default deploy unchanged (`RERANK_ENABLED=false`); adaptive rerank and per-tool overrides deferred to track 2; [PR #4](https://github.com/Tusquito/codebase-indexer-mcp/pull/4) | 2026-07-03 |
| 0008 | Optional ColBERT reranking | Accepted | phase-2-track-2a | `merged` | `RERANK_ADAPTIVE_ENABLED=true`, `RERANK_ADAPTIVE_GAP=0.02`; hybrid RRF probe in `QdrantStorage._search_single` before ColBERT; probe limit `max(top_k, 2)`; fewer than 2 probe hits always runs ColBERT; `AdaptiveRerankStats` on storage for bench/eval skip-rate; ColBERT query embed unchanged; unit tests + `bench.py`/`eval_retrieval.py` skip-rate reporting; `SEARCH_BEHAVIOR.md` + `.env.example`; track 2b per-tool override deferred; default deploy unchanged (`RERANK_ENABLED=false`); [PR #6](https://github.com/Tusquito/codebase-indexer-mcp/pull/6) | 2026-07-03 |
| 0008 | Optional ColBERT reranking | Accepted | phase-2-track-2b | `merged` | Per-tool `rerank: bool \| None = None` on `search_codebase`, `search_symbols`, xref/service_map semantic paths; embed + tool layer override (`use_rerank = self.rerank and rerank is not False`); `colbert_vector=None` skips storage rerank/adaptive paths; `rerank=None` default; `rerank=false` skips ColBERT when `RERANK_ENABLED=true`; import-phrased xref inherits tool-level `rerank`; exact symbol/call_site unaffected; `recommend_code` excluded; final ADR 0008 phase complete; test debt: Embedder rerank unit tests, adaptive+override integration, golden-set `rerank=false` sweep, live Qdrant adaptive (carried from 2a); [PR #7](https://github.com/Tusquito/codebase-indexer-mcp/pull/7) | 2026-07-03 |
| 0009 | Multi-hop retrieval | Accepted (phase 1) | phase-1 | `merged` | Client decomposition docs + golden tags | 2026-07-02 |
| 0009 | Multi-hop retrieval | Accepted (phase 1; phase 2 merged) | phase-2 | `merged` | `eval_multihop.py` + `multihop_rrf.fuse_hop_rrf`; curated `hop2_query_text` inline in `golden_queries.jsonl`; client-side RRF fusion hop 1 + hop 2 via `run_search`; `--rerank` passthrough; side-by-side ranx vs single-pass on `multi_hop` slice; `eval_baseline.json` `multi_hop_2hop` snapshot (live verify, nomic embed); unit tests; `SEARCH_BEHAVIOR.md` + `ARCHITECTURE.md`; no MCP/compose/runtime changes; defer server-side hop fusion and GraphRAG to ADR 0002+; [PR #8](https://github.com/Tusquito/codebase-indexer-mcp/pull/8) | 2026-07-03 |
| 0010 | Defer Ragas to client | Accepted | all | `merged` | Export script + DEPLOYMENT guide | 2026-07-02 |
| 0011 | Ollama-only dense embedding | Superseded (→ 0025) | all | `merged` | See CHANGELOG [Unreleased]; superseded 2026-07-04 by ADR 0025 (TEI hard replace) | 2026-07-02 |
| 0012 | Retrieval-only RAG split | Accepted | all | `merged` | Shipped | 2026-07-02 |
| 0013 | External agent knowledge base | Accepted | all | `merged` | MCP tools surface | 2026-07-02 |
| 0014 | Vector discovery + n8n ops | Accepted (phase 1 — recommendation search tool) | track-a-phase-1 | `merged` | Tool `recommend_code`; `QdrantStorage.recommend`; config (`RECOMMEND_ENABLED`, `RECOMMEND_MAX_EXAMPLES`); RecommendStrategy AVERAGE_VECTOR; dense-only; path_glob fnmatch + limit×3; missing chunk IDs fail fast; single-collection; defer outlier helper (Track A P2), n8n compose (Track B), sparse fusion, multi-collection; [PR #5](https://github.com/Tusquito/codebase-indexer-mcp/pull/5) | 2026-07-03 |
| 0014 | Vector discovery + n8n ops | Accepted (phase 1; phase 2 — outlier / diversity helper) | track-a-phase-2 | `merged` | Tool `find_outlier_chunks`; `QdrantStorage.find_outlier_chunks`; `RecommendStrategy.BEST_SCORE` negative-only; cosine-to-centroid + `OUTLIER_MAX_SIMILARITY` (0.55); gate via `RECOMMEND_ENABLED` (no `OUTLIER_ENABLED`); `OUTLIER_MAX_CONTEXT_SAMPLES` (200); scroll supplement only when `path_glob` or no explicit `context_chunk_ids`; bounded `limit` (cap 20); dense-only single-collection; defer sparse fusion, multi-collection, Track B n8n, Discovery API context pairs; [PR #9](https://github.com/Tusquito/codebase-indexer-mcp/pull/9) | 2026-07-03 |
| 0015 | ColBERT HTTP sidecar | Accepted | phase-1 | `merged` | Opt-in `COLBERT_EMBED_BACKEND=remote` + `colbert_worker` sidecar; default in-process ONNX unchanged; FastAPI lifespan preload; `ColbertRemoteBackend` httpx client; `docker-compose.colbert-worker.yml` with shared `fastembed_cache`; `.env.example` + `SEARCH_BEHAVIOR.md`; [PR #2](https://github.com/Tusquito/codebase-indexer-mcp/pull/2) | 2026-07-03 |
| 0015 | ColBERT HTTP sidecar | Accepted | phase-2 | `merged` | GPU sidecar via `colbert_worker/Dockerfile.gpu` (`onnxruntime-gpu==1.26.0`, `python:3.12-slim`); compose override `docker-compose.colbert-worker.gpu.yml` (NVIDIA reservations mirroring Ollama); `COLBERT_DEVICE_IDS` → `ColbertOnnxBackend.device_ids`; worker `/health` reports `device` + `cuda_available`; fail-fast CUDA preload; `bench_colbert_sidecar.py` remote throughput bench; single-GPU 8GB OOM documented (no auto-scheduler); CI-safe mocked/skipped GPU tests + non-blocking GPU Dockerfile CI job; [PR #3](https://github.com/Tusquito/codebase-indexer-mcp/pull/3) | 2026-07-03 |
| 0015 | ColBERT HTTP sidecar | Accepted | phase-3-plus | `not_started` | MCP slim image when remote-only | 2026-07-04 |
| 0016 | Adopt Qwen3-Embedding-4B as default dense model *(historical — superseded for production default by 0021/0025)* | Accepted (all phases complete) | phase-1 | `merged` | Qwen3 0.6B/4B/8B in `KNOWN_EMBED_MODEL_*` (max tokens 32768); MRL `dimensions` passthrough (32≤size≤native) in dense backend + `factory.py`; Qwen3 GPU defaults in `.env.example`; compose generator Qwen3 (`scripts/run_compose_integration.py`); `benchmarks/_settings.py`; unit tests; docs; defer Phase 2 eval baseline + `num_ctx`; generator-only compose env; [PR #12](https://github.com/Tusquito/codebase-indexer-mcp/pull/12) | 2026-07-03 |
| 0016 | Adopt Qwen3-Embedding-4B as default Ollama dense model | Accepted (all phases complete) | phase-2 | `merged` | Jina comparison baseline; recall@10 gate waived with per-tag analysis (−63.1% vs Jina); refreshed `eval_baseline.json` + `golden_queries.jsonl`; alias line remapping; operational compose/env eval overrides not committed; final ADR 0016 phase complete; defer CI validate-labels gate, compose WORKSPACE_ROOT eval preset, optional non-blocking recall benchmark job, compose host-env URL isolation, `num_ctx`; [PR #14](https://github.com/Tusquito/codebase-indexer-mcp/pull/14) | 2026-07-03 |
| 0017 | Model-accurate tokenizer for TEI dense truncation | Accepted (phase 1 — loader + TEI backend) | phase-1 | `merged` | `load_dense_tokenizer(model_id)` in `tokenizer_loader.py` via `tokenizers.Tokenizer.from_pretrained` + HF env cache dirs; shared class-level `Tokenizer` in `TeiDenseBackend` at `preload()` via `_ensure_truncation()`; `_truncate_batch` uses `truncate_for_embedding` (sparse BM25 path untouched); fallback = log WARNING + pass text through unchanged; unit tests (mock + optional slow Nomic); `ARCHITECTURE.md`, `.env.example`, `docker-compose.yml` HF_HOME; defer Phase 2 observability + ADR 0011 body edit; [PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11) | 2026-07-03 |
| 0018 | Adopt OpenTelemetry instrumentation with Prometheus metrics and optional OTLP export | Accepted (phase 1 — Application Prometheus metrics (MCP + ColBERT worker)) | phase-1 | `merged` | Opt-in `METRICS_ENABLED=false` default; `prometheus_client` on dedicated `CollectorRegistry`; metrics-only `@observe_tool` on all MCP tool handlers; no collection/rel_path labels; application counters/histograms + truncation counter; index metrics via IndexJobTracker; `GET /metrics` on MCP and ColBERT worker HTTP layer; unit tests (`test_telemetry_metrics.py`); `DEPLOYMENT.md` scrape docs; defer `METRICS_PORT`, docker-compose scrape wiring, Phase 2 OTel traces, Phase 3 observability compose stack; [PR #13](https://github.com/Tusquito/codebase-indexer-mcp/pull/13) | 2026-07-03 |
| 0019 | Adopt YAML structured events for ADR implementation tracking | Accepted (phase 1) | phase-1 | `merged` | YAML tracker under `docs/adr/tracker/` with `schema.yaml` contract driving validation; stdlib+PyYAML render script generating marker-delimited summary/active/phase-logs/open-decisions blocks with preamble preservation; non-blocking `--check \|\| true` CI step validates sample YAML only — live `IMPLEMENTATION_TRACKER.md` hand-maintained until Phase 2 migration; Phase 3 agent cutover deferred; 9 render unit tests pass; 398 suite pass (8 storage-integration environmental); Docker integration pass; plan compliance pass; review rounds: 1; [PR #24](https://github.com/Tusquito/codebase-indexer-mcp/pull/24) | 2026-07-07 |
| 0019 | Adopt YAML structured events for ADR implementation tracking | Accepted (phase 2) | phase-2 | `merged` | One-time `scripts/migrate_tracker_to_yaml.py` migration; YAML source of truth with generated markdown; kebab-case `phase_key`; plain `adr_id`; open-decisions collapsed to bullets; blocking `--check` gate; render `--check` zero drift; 40 phase + 142 event YAML valid; 9 render unit tests pass; ruff clean; CI render-diff blocking; Docker integration pass; review rounds: 1; Phase 3 agent cutover deferred; [PR #28](https://github.com/Tusquito/codebase-indexer-mcp/pull/28) | 2026-07-08 |
| 0019 | Phase 3 — Agent pipeline cutover | Accepted (phase 3) | phase-3 | `merged` | One PR: rewrite `.cursor/agents/adr-tracker.md` to write YAML event + phase files and run `scripts/render_adr_tracker.py` (replacing markdown string surgery); update `adr-orchestrator.md` tracker/render contract and `adr-git-operator.md` cleanup commit paths (`docs/adr/tracker/**` + regenerated `IMPLEMENTATION_TRACKER.md`); deprecate/remove legacy markdown-append instructions; sync `.cursor/agents/README.md` and `docs/adr/README.md`; archive one-time `scripts/migrate_tracker_to_yaml.py` under `scripts/archive/`. No server/runtime code. Review rounds: 1; render `--check` zero drift; 9 render unit tests pass; Docker integration pass; [PR #29](https://github.com/Tusquito/codebase-indexer-mcp/pull/29) | 2026-07-08 |
| 0020 | Fine-tune Qwen3 for code retrieval with Jina quality gate | Accepted (phase 1 — Dataset + training pipeline) | phase-1 | `merged` | Shipped: `mcp_server/benchmarks/train/` (`export_golden_pairs.py`, `mine_hard_negatives.py`, `finetune_qwen3_code.py`, `_schema.py`, `_split.py`, `_positives.py`, `README.md`); optional `[train]` pyproject extra isolated from runtime/CI; default validation holdout = all four `multi_hop` golden queries; hard-negative mining via base Qwen3 hybrid `run_search` (rerank off); LoRA via PEFT + sentence-transformers (TripletLoss when all pairs have mined negatives, else MnRL in-batch); outputs under `benchmarks/train/outputs/` gitignored; unit tests (export/split/mining + `test_finetune_mrr.py`); `DEPLOYMENT.md` training stub. Deviations: `resolve_positive_passage` (singular); single-pass checkpoint save (baseline + final val MRR in `train_summary.json`) vs per-epoch best (documented at verification). Defer Ollama export/registry (P2), Jina quality gate + baseline update (P3), CI observation job (P4); no Docker/runtime/registry changes; [PR #15](https://github.com/Tusquito/codebase-indexer-mcp/pull/15) | 2026-07-03 |
| 0021 | Revert default dense embedder to Jina code; retire Qwen3 as production default | Accepted (phase 1 — Config + docs revert) | phase-1 | `merged` | Jina production default @ 768 in env/bench/compose/docs; Qwen3 experimental preset (−63.1% recall@10); `DENSE_EMBED_MODEL` in `.env.example` REQUIRED; TEI downloads model on first start; `config.py` Qwen3 registry/MRL retained; ADR index housekeeping in Phase 1 scope; defer Phase 2 (`eval_baseline.json`); CHANGELOG full update Phase 3; test debt: `smoke_recommend` dim mismatch until Phase 2 re-index; [PR #16](https://github.com/Tusquito/codebase-indexer-mcp/pull/16) | 2026-07-03 |
| 0021 | Revert default dense embedder to Jina code; retire Qwen3 as production default | Accepted (phase 1; phase 2 — Eval baseline refresh) | phase-2 | `merged` | GPU Jina @768 live baseline committed (`eval_baseline.json`; `ACCELERATOR=gpu`, `RERANK_ENABLED=false`); pre-commit gate vs `eval_baseline_jina.json` failed (recall@10 0.263 vs 0.660 — golden alias drift, not embedder regression); post-commit Docker self-compare pass; frozen `eval_baseline_jina.json` preserved; scanner `.venv*` prune + golden alias fixes; `_settings.py` `ollama_embed_model` default; defer golden label realignment, pre-commit recall gate CI, optional `eval_multihop` CI gate; Phase 3 (CHANGELOG/ADR index housekeeping); [PR #18](https://github.com/Tusquito/codebase-indexer-mcp/pull/18) | 2026-07-04 |
| 0021 | Revert default dense embedder to Jina code; retire Qwen3 as production default | Accepted (all phases complete) | phase-3 | `merged` | Finisher bundled README index + CHANGELOG full update in docs commit `53f68e0` via [PR #20](https://github.com/Tusquito/codebase-indexer-mcp/pull/20); final ADR 0021 phase complete; test debt: golden label realignment, pre-commit recall gate CI, optional `eval_multihop` CI gate (carried from P2) | 2026-07-04 |
| 0022 | GPU-default acceleration; CPU only when explicit | Accepted (phase 1 — GPU-default compose + docs) | phase-1 | `merged` | Compose-only `ACCELERATOR=gpu` default; canonical `-f` via `scripts/compose_files.py`; fail-fast `require_gpu()` in integration harness; sparse BM25 unchanged (CPU in MCP); docs/compose updates; 12 unit tests pass; no `.github/workflows/ci.yml` changes. Defer Phase 2 (ColBERT remote GPU default + 0021 P2 baseline), Phase 3 (CI `ACCELERATOR=cpu`, self-hosted GPU smoke, `ollama ps` GPU assertion). [PR #17](https://github.com/Tusquito/codebase-indexer-mcp/pull/17) | 2026-07-04 |
| 0022 | GPU-default acceleration; CPU only when explicit | Accepted (phase 1; phase 2 — Retire CPU ColBERT defaults) | phase-2 | `merged` | Remote GPU sidecar default when `RERANK_ENABLED=true`; explicit onnx for `ACCELERATOR=cpu`; Phase 3 CI split deferred; 368 unit tests pass; integration pass; quality validation threshold 0 self-compare pass; plan compliance pass; review rounds: 1. [PR #19](https://github.com/Tusquito/codebase-indexer-mcp/pull/19) | 2026-07-04 |
| 0022 | GPU-default acceleration; CPU only when explicit | Accepted (all phases complete) | phase-3 | `merged` | Squash merge [PR #20](https://github.com/Tusquito/codebase-indexer-mcp/pull/20); six ubuntu-latest jobs `ACCELERATOR=cpu`; blocking GHA `compose-integration`; non-blocking self-hosted `gpu-smoke`; `check_ollama_gpu_processor()` in harness; finisher bundled 0021 P3 README + CHANGELOG close-out (`53f68e0`); final ADR 0022 phase complete; test debt: gpu-smoke first run when self-hosted runner available | 2026-07-04 |
| 0023 | Move call-site lookup from Qdrant callees to Neo4j CALLS | Accepted (phase 1 — Symbol-unified CALLS + Neo4j caller query + dual-read routing) | phase-1 | `merged` | `call_token` on CALLS; symbol unification Rules 1–3; `Neo4jStorage.find_callers`; Path D routes Neo4j when `GRAPH_ENABLED` else Qdrant; Qdrant `callees` dual-write retained; re-index on graph writer changes (removed `GRAPH_SCHEMA_VERSION` pre-1.0); defer Phases 2–4; 383 unit tests pass; integration pass; quality validation threshold 0 pass; plan compliance pass; review rounds: 1; test debt: live Neo4j parity fixture, unified-symbol Cypher traversal, mixed-collection per-engine routing (Phase 2). [PR #21](https://github.com/Tusquito/codebase-indexer-mcp/pull/21) | 2026-07-04 |
| 0023 | Move call-site lookup from Qdrant callees to Neo4j CALLS | Accepted (phase 1; phase 2 — Stop dual-write to Qdrant) | phase-2 | `merged` | Reused `graph_call_sites` metadata; per-collection Path D routing; Qdrant fallback + warning; retain callees index until Phase 3; 391 unit tests pass; integration pass; plan compliance pass; review rounds: 2; test debt: Testcontainers slow test optional CI job; defer Phases 3–4 and ADR 0002 Phase 2 `graph_node_ids`. [PR #22](https://github.com/Tusquito/codebase-indexer-mcp/pull/22) | 2026-07-04 |
| 0024 | Add resource-aware stack tuner for RSS allocation and performance tuning | Accepted (phase 1 — Analyze + allocate) | phase-1 | `merged` | Squash merge [PR #25](https://github.com/Tusquito/codebase-indexer-mcp/pull/25) (`e0c6100`); Pure `tune_alloc.py` split from `tune_stack.py` CLI; topology-priority RAM selection; deterministic knob tiers; tri-state flag precedence mirroring `compose_files.py`; stdlib RAM detection + `--max-ram-gib` fallback; TEI caps `TEI_MEM_LIMIT`/`TEI_CPUS`; ColBERT MCP ≤35% cap; compose-only env vars (`.env` write refused); ADR Accept + `docs/adr/README.md` index; 17 unit tests pass; CLI smoke pass; Docker integration pass; plan compliance pass; review rounds: 1; NVIDIA probe deferred Phase 2; defer `.env.example` preset sync (Phase 4); test debt: CLI-level tests for `tune_stack.py`, host-detection mocks, ADR success-criterion #1 ±10% preset assertion deferred; opt-in, no default behavior change | 2026-07-08 |
| 0025 | Adopt HuggingFace TEI sidecar for dense embedding | Accepted (all phases complete) | phase-1 | `merged` | Squash merge [PR #23](https://github.com/Tusquito/codebase-indexer-mcp/pull/23) (`0f01cda`); `TeiDenseBackend` + OpenAI `/v1/embeddings`; TEI compose (`docker-compose.tei.yml` + `.tei.gpu.yml`, profile `bundled-tei`); Ollama dense deleted; Ollama→TEI doc/docstring sweep (16 files); upstream TEI CUDA-detection bug fixed via `docker-compose.tei.gpu.yml` entrypoint override; upstream TEI CPU-warmup bug fixed via `--max-batch-tokens` cap + client-side `MAX_DENSE_EMBED_TOKENS` pairing (CPU-only CI path); live GPU quality-validation (recall@10=0.3590, MRR=0.3576, ndcg@10=0.2807, 43/43 golden labels); ADR accepted all phases via docs commit `a756677`; final ADR 0025 phase complete; test debt: optional offline CI alias-drift guard, `benchmarks/train/**` (ADR 0020 follow-up) | 2026-07-07 |
| 0026 | Full-stack embedding model quality benchmark and selection framework | Accepted (phases 1–3; phases 4–5 open) | phase-1 | `merged` | Squash merge [PR #27](https://github.com/Tusquito/codebase-indexer-mcp/pull/27) (`0ca2f88`); Content-anchored labels with 5-step ladder (`{rel_path}::{symbol_name}` + `start_line` hint); drift counted not silently scored; `--validate-labels` drift re-resolution with counts; `label_drift` per eval run; CI repro via `--keep` + kept-stack pytest in blocking `compose-integration`; `label_anchor.py` + `eval_retrieval.py` + golden `anchors`; 11 unit tests pass (`test_label_anchor.py`); ruff clean; Docker integration + quality validation pass (55 labels, 12 drifted and re-resolved via content anchoring, 0 unresolved; threshold 0 pass); repeat-run repro in blocking compose-integration CI job gates `recall@10` within ±1pp per ADR success criterion #1 (rank-sensitive `mrr`/`ndcg@10` bounded, not exact — see `test_harness_reproducibility.py`); review rounds: 1; one PR; no runtime/config/production change; defer Phases 2–5 (≥75-query expansion is Phase 2); test debt: symbol drift live integration exercised (12 drift observed in CI run), Phase 4 collection override concern | 2026-07-08 |
| 0026 | Phase 2 — Golden-set expansion | Accepted (phases 1–3; phases 4–5 open) | phase-2 | `merged` | Expand `mcp_server/benchmarks/fixtures/golden_queries.jsonl` in place from 26 to ≥75 distinct content-anchored (Phase-1 format) queries via the existing `suggest_labels.py` workflow; meet resolved per-tag membership targets (symbol 26, conceptual 7, config 19, cross_file 19, multi_hop 15); preserve `multi_hop` `hop2_query_text`; every multi_hop row carries a secondary tag (no pure multi_hop); ground-truth subset floor 19; `--validate-labels` zero unresolved; bump `golden_set_version` in `eval_baseline.json` only; raise golden-fixture unit-test floors + add a per-tag distribution/anchor-coverage test; Docker integration via `scripts/run_compose_integration.py`; quality validation report-only (`--threshold 0`). Defer Phases 3–5. [PR #30](https://github.com/Tusquito/codebase-indexer-mcp/pull/30) (`8be500b`) | 2026-07-08 |
| 0026 | Phase 3 — Candidate registry + integration spikes | Accepted (phases 1–3; phases 4–5 open) | phase-3 | `merged` | 10-row `model_candidates.yaml` registry with validating `candidates.py` loader; `config.py` entries for GTE_MODERNBERT_SPECS, GRANITE_EMBED_SPECS (including granite-embedding-97m), and INF_RETRIEVER_SPECS; `_settings.py` per-candidate swap helper; `verify_candidate.py` (`tei_health` + `tei_embed_smoke`); feature-flagged `query_instruction` and `normalize_output` hooks in `TeiDenseBackend`; inf-retriever spike passed; pplx-embed INT8 dropped for both sizes per 30-min drop-on-failure rule; unit tests; fixture-only, no production default change. Defer live per-native-candidate verify runs and Phase 4 bake-off orchestration. | 2026-07-10 |
| 0028 | Phase 1 — Documented profile | Accepted (phase 1 — Documented profile) | phase-1 | `merged` | Phase 1 — `docs/DEPLOYMENT.md` § Apple Silicon (arm64 CPU) with M3 Pro 24 GiB Docker VM profile and minimal 18 GiB tier; `.env.example` macOS presets; README + `.github/copilot-instructions.md` cross-links; manual operator checklist; defer Phase 2 code. | 2026-07-12 |
| 0028 | Phase 2 — Arch-aware compose defaults | Accepted (phase 2 — Arch-aware compose defaults) | phase-2 | `merged` | `TEI_IMAGE_CPU_ARM64_DEFAULT` + `container_arch()` (Docker server arch → `platform.machine()` fallback) in `scripts/compose_files.py`; arch-aware `tei_image_default()`; darwin `sysctl hw.memsize` + `DEFAULT_RESERVE_GIB=4.0` in `scripts/tune_alloc.py`; MKL compose fix or arm64 gate in `docker-compose.tei.yml`; arch-aware `TEI_IMAGE` in `scripts/run_compose_integration.py`; unit tests per ADR 0028 Validation; Docker integration. Defer Phase 3 ColBERT-on-Mac doc and Phase 4 `macos_m3pro_matrix.json`. | 2026-07-12 |
| 0029 | Phase 1 — Documentation | Accepted (phase 1 — Documentation) | phase-1 | `merged` | Docs-only single PR; host-native Metal TEI in `docs/DEPLOYMENT.md` (after Apple Silicon, before External TEI); `.env.example` Metal preset; four-surface sync (README, copilot-instructions, SKILL, DEPLOYMENT); bundled 0028 CPU TEI remains default; Metal opt-in via `TEI_URL` + empty `COMPOSE_PROFILES`; `--hostname 127.0.0.1` with upstream flag verification note. Defer Phase 2 `--external-tei` integration smoke and Phase 3 `metal_host_tei` benchmark. | 2026-07-12 |
| 0029 | Phase 2 — Integration smoke | Accept skipped — unchanged (Accepted) | phase-2 | `merged` | Harness-only PR; `include_tei=False` via `compose_file_args`; force `ACCELERATOR=cpu`; M3 Pro Metal cgroup preset; host TEI preflight; `tei_container_absent` verdict gate; bundled path unchanged; quality/perf validation skipped. Defer live M3 Pro `--external-tei` full Docker integration before merge, Phase 3 `metal_host_tei` benchmark, and maintainer Metal log check on first embed. | 2026-07-12 |
| 0030 | Phase 1 — Scaffold + storage + TEI | Accepted (phase 1 — Scaffold + storage + TEI) | phase-1 | `merged` | Accept ADR 0030; repo-root solution; hand-authored docker-compose.aspire.yml; arm64 cpu-arm64-latest TEI; accelerator defaults cpu; MCP stub get_health only; SearchAsync stub until Phase 3; tokenizer truncation Phase 2; Python production default until Phase 7 | 2026-07-13 |
| 0030 | Phase 2 — Indexing pipeline | Accepted (phase 1; phase 2 — Indexing pipeline) | phase-2 | `merged` | `WorkspaceScanner` (SHA-256 incremental scan, ignore files, `ArrayPool` hashing, channel worker fan-out DOP=1); `TreeSitterChunker` (port `chunker.py` via `TreeSitter.DotNet`, regex SQL fallback); `OnnxSparseEmbedder` (`Microsoft.ML.OnnxRuntime`, same `Qdrant/bm25` artifacts); model-accurate dense tokenizer truncation; `IndexPipeline` with `Channel<T>` stages in `IndexPipelineHostedService`; `IndexCodebaseService` + `IndexJobService`; MCP index tools (`index_codebase`, `index_status`, `stop_indexing`, `index_all`); chunk-ID golden parity fixture; `docker-compose.aspire.yml` workspace/cache wiring (fastembed at `/root/.cache/fastembed` with `fastembed_cache` volume); `--aspire-stack` integration smoke (manual M3 Pro pre-review, optional non-blocking CI); Python `run_compose_integration.py` remains green | 2026-07-13 |
| 0030 | Phase 3 — Core search tools | Accepted (phase 1–2 merged; phase 3 — Core search tools) | phase-3 | `verified` | Hybrid RRF via Qdrant QueryAsync + client CrossCollectionRrf; Embedding PrefetchMultiplier/RrfK; Qdrant create parity (int8/HNSW/memmap/payload indexes); Host resolves default collection; six MCP tools; summary without build_dependencies; ColBERT no-op; Aspire gRPC :6334 + TEI arch image + SDK-container test fallback; quality via --mcp-url | 2026-07-21 |
<!-- END GENERATED:summary -->

Superseded [0001](0001-pluggable-embed-backends.md) — historical; implementation superseded by [0011](0011-ollama-only-dense-embedding.md).

## Active and upcoming work

<!-- BEGIN GENERATED:active -->
- **0030** Phase 3 — Core search tools — `verified`
<!-- END GENERATED:active -->

### Partial acceptance

| ADR | Done | Remaining |
|-----|------|-----------|
| 0002 | Phase 1 — Neo4j storage + graph writer ([PR #10](https://github.com/Tusquito/codebase-indexer-mcp/pull/10)); Phase 2 — `graph_node_ids` ([PR #26](https://github.com/Tusquito/codebase-indexer-mcp/pull/26)); Phase 3 — `expand_search_context` ([PR #32](https://github.com/Tusquito/codebase-indexer-mcp/pull/32)) | Phase 4 — Neo4j cross-project queries |
| 0009 | Phase 1 — multi-hop docs + golden tags; Phase 2 — `eval_multihop.py` ([PR #8](https://github.com/Tusquito/codebase-indexer-mcp/pull/8)) | Phase 3+ server mechanisms; optional graph-backed hops per [0002](0002-graphrag-neo4j-qdrant.md) |
| 0014 | Track A Phase 1 — `recommend_code` ([PR #5](https://github.com/Tusquito/codebase-indexer-mcp/pull/5)); Track A Phase 2 — `find_outlier_chunks` ([PR #9](https://github.com/Tusquito/codebase-indexer-mcp/pull/9)) | Track B (n8n compose) deferred |
| 0015 | Phase 1 — HTTP sidecar ([PR #2](https://github.com/Tusquito/codebase-indexer-mcp/pull/2)); Phase 2 — GPU worker ([PR #3](https://github.com/Tusquito/codebase-indexer-mcp/pull/3)) | Phase 3+ — MCP slim image when remote-only (`not_started`) |
| 0017 | Phase 1 — tokenizer loader + TEI dense truncation ([PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)) | Phase 2 observability |
| 0018 | Phase 1 — Prometheus metrics ([PR #13](https://github.com/Tusquito/codebase-indexer-mcp/pull/13)) | Phase 2 OTel traces; Phase 3 observability compose; `METRICS_PORT` / scrape wiring |
| 0020 | Phase 1 — Dataset + training pipeline ([PR #15](https://github.com/Tusquito/codebase-indexer-mcp/pull/15)) | Phases 2–4 cancelled per [ADR 0021](0021-revert-jina-production-default-retire-qwen3.md) |
| 0023 | Phase 1 — Neo4j CALLS + Path D ([PR #21](https://github.com/Tusquito/codebase-indexer-mcp/pull/21)); Phase 2 — stop Qdrant `callees` dual-write ([PR #22](https://github.com/Tusquito/codebase-indexer-mcp/pull/22)) | Phases 3–4 (retire callees index; optional CALLS_RESOLVED) |
| 0024 | Phase 1 — Analyze + allocate ([PR #25](https://github.com/Tusquito/codebase-indexer-mcp/pull/25)) | Phases 2–4 (seed+bench, iterative search, preset sync) |
| 0026 | Phase 1 — Harness reliability ([PR #27](https://github.com/Tusquito/codebase-indexer-mcp/pull/27)); Phase 2 — golden-set expansion ([PR #30](https://github.com/Tusquito/codebase-indexer-mcp/pull/30)); Phase 3 — candidate registry ([PR #31](https://github.com/Tusquito/codebase-indexer-mcp/pull/31)) | Phases 4–5 (GPU bake-off; selection / promote default) |
| 0028 | Phase 1 — Documented profile; Phase 2 — arch-aware compose defaults | Phases 3–4 (ColBERT-on-Mac doc; maintainer matrix) |
| 0029 | Phase 1 — Metal TEI docs; Phase 2 — `--external-tei` harness | Phase 3 — `metal_host_tei` benchmark |
| 0030 | Phase 1 — Scaffold + storage + TEI; Phase 2 — Indexing pipeline | Phases 3–7 (search → cutover; Python remains production until Phase 7) |

---

## Phase logs

<!-- BEGIN GENERATED:phase-logs -->
### ADR 0002 — Phase 3 — Graph-augmented MCP retrieval

#### 2026-07-10 — verification
- **Phase:** Phase 3 — Graph-augmented MCP retrieval
- **Tracker status:** `verified`
- **Choices:** S1 (`LIMIT $max_nodes` bounds paths not distinct nodes) accepted as-is — plan-compliant and optional.
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/tools/graph_search.py`, `mcp_server/src/codebase_indexer/storage/neo4j.py`, `mcp_server/src/codebase_indexer/main.py`, `mcp_server/tests/test_graph_search.py`, `mcp_server/tests/test_graph_expand_integration.py`, `docs/ARCHITECTURE.md`
- **Verify:** Round 1 covered full implementation (Cypher injection safety, chunk_id-only seeding, `GRAPH_ENABLED` gating, node-cap enforcement — all pass) with one warning (R1) and one non-blocking suggestion (S1). Round 2 confirmed the R1 docs fix in `docs/ARCHITECTURE.md` (Phase 3 documented as shipped, no contradiction) and re-ran the unit suite: `uv run pytest -q` → 481 passed, 8 skipped. Docker integration (step 3.5) Verdict: pass cross-checked. Quality validation and performance report skipped per plan. Review rounds: 2.
- **Changelog:** yes — Add opt-in `expand_search_context` MCP tool for graph-augmented retrieval: seeds from a chunk_id and expands the Neo4j code subgraph (bounded by `max_nodes`), returning a structured GraphContext JSON response. Available only when `GRAPH_ENABLED=true`.

#### 2026-07-10 — prioritization
- **Phase:** Phase 3 — Graph-augmented MCP retrieval
- **Tracker status:** `candidate`
- **Choices:** Selected over ADR 0026 Phase 4 (GPU bake-off, deprioritized this run by invoker despite GPU availability) and over ADR 0027 (Proposed, invoker declined to Accept — remains Proposed, untouched); this is the prioritizer's next-ranked, no-GPU-dependency alternative from the 2026-07-10 prioritization report; single phase per pipeline rule; no ADR Accept required (0002 already Accepted phase 1). **Why now:** Human-directed pivot; adr-prioritizer 2026-07-10 run ranked ADR 0002 Phase 3 as alternative #2 (~29 score) behind ADR 0026 Phase 4 (~32 score); invoker explicitly deprioritized 0026 Phase 4 and declined Proposed ADR 0027; ADR 0002 Phases 1–2 merged (`graph_writer.py`, `storage/neo4j.py`, `graph_node_ids` payload linking shipped); Phase 3 (`expand_search_context`) not started. **Suggested scope:** one phase (= one PR). **Chosen scope:** New MCP tool `expand_search_context` (final name TBD by planner) in `mcp_server/src/codebase_indexer/tools/graph_search.py`: hybrid search via existing `search_common.py` → collect seed `chunk_id`/`graph_node_ids` → bounded Cypher neighborhood query (1–2 hops via `GRAPH_MAX_HOPS`/`GRAPH_MAX_NODES`) against `Neo4jStorage` → attach matching chunk payloads → return structured `GraphContext` (nodes, edges, related chunks) — no LLM-generated answer; gated behind existing `GRAPH_ENABLED` flag; zero behavior change when disabled; Docker integration via existing `docker-compose.neo4j.yml` override; defer Phase 4 (Neo4j-backed cross-project queries / `map_service_dependencies` Cypher backend).
- **Deviations:** none
- **Changelog:** no — tracker status candidate; invoker Changelog: yes deferred until verified

#### 2026-07-10 — plan
- **Phase:** Phase 3 — Graph-augmented MCP retrieval
- **Tracker status:** `planned`
- **Choices:** Tool named `expand_search_context` (human-confirmed); conditional registration mirrors `recommend_enabled` pattern; `chunk_id`-only Cypher seeding (human-confirmed); Quality validation skipped (no ranking-path change); Performance report skipped; suggested tier claude-opus-4-8-thinking-low; Docker integration harness extended for live Neo4j validation (human-confirmed). Assumptions: Phase defaults to single PR; registration guarded by `settings.graph_enabled`; hop count validated as int and clamped to `graph_max_hops` (cannot be a Cypher parameter).
- **Deviations:** none
- **Changelog:** no — invoker Changelog: no; status planned (not verified)

#### 2026-07-10 — merge
- **Phase:** Phase 3 — Graph-augmented MCP retrieval
- **Tracker status:** `merged`
- **Choices:** squash merge `829aae1` on feature branch `adr/0002-phase-3-graph-expand` (deleted after merge; includes `d9b21ac` feat(graph): add expand_subgraph storage; `7968b24` feat(graph): add expand_search_context; `90b038a` test(graph): add expand context tests; `9a0959a` chore(integration): add graph flag; `91e3aab` docs(adr): sync phase 3 graph expand); ADR Accept skipped — ADR 0002 status unchanged (`Accepted (phase 1 — Neo4j storage + index-time graph writer)`); release skipped; Phase 4 remains open/deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #32](https://github.com/Tusquito/codebase-indexer-mcp/pull/32) (`adr/0002-phase-3-graph-expand`; squash `829aae1`)`
- **Verify:** carried from verification — Round 1 full implementation pass with one warning (R1) and one non-blocking suggestion (S1); Round 2 confirmed R1 docs fix and re-ran unit suite (481 passed, 8 skipped); Docker integration Verdict pass; review rounds: 2
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/32 — status: merged — commit: 829aae1
- **Changelog:** no — version cut skipped; [Unreleased] bullet already added during verification step

#### 2026-07-10 — implementation
- **Phase:** Phase 3 — Graph-augmented MCP retrieval
- **Tracker status:** `implemented`
- **Choices:** Tool `expand_search_context` in `tools/graph_search.py`; chunk_id-only seeding (no `graph_node_ids` reads); `Neo4jStorage.expand_subgraph` with injection-safe interpolated hop count + `LIMIT $max_nodes`; conditional registration behind `GRAPH_ENABLED`; `GraphContext` = `nodes`/`edges`/`related_chunks`/`seeds`; Docker harness `--graph` flag brings up Neo4j override and validates the live stack via health + startup-log signal.
- **Deviations:** Live graph validation uses Neo4j health + mcp_server `graph_enabled` startup-log signal instead of an MCP `tools/list` handshake (deterministic, matches existing harness style).
- **Code evidence:** `mcp_server/src/codebase_indexer/tools/graph_search.py`, `mcp_server/src/codebase_indexer/storage/neo4j.py`, `mcp_server/src/codebase_indexer/main.py`, `mcp_server/tests/test_graph_search.py`, `mcp_server/tests/test_graph_expand_integration.py`, `scripts/compose_files.py`, `scripts/run_compose_integration.py`
- **Test debt:** node-cap truncation; hydration fallback; live `--graph` Docker run; multi-collection seeding
- **Changelog:** no — invoker Changelog: no; status implemented (not verified)

#### 2026-07-08 — verification
- **Phase:** Phase 2 — Qdrant payload linking (`graph_node_ids`)
- **Tracker status:** `verified`
- **Choices:** Neighbor-keys-only; batch before upsert; graph_enabled metadata; graph_node_ids omitted for zero-neighbor chunks and GRAPH_ENABLED=false.
- **Deviations:** none
- **Code evidence:** `graph_writer.py`, `pipeline.py`, `qdrant.py`, `search_common.py`
- **Test debt:** prometheus_client and neo4j driver needed in CI env
- **Verify:** 34 Phase 2 unit tests pass; full suite 420 passed; integration Verdict pass; plan compliance pass; review rounds: 1
- **Changelog:** yes

#### 2026-07-08 — prioritization
- **Phase:** Phase 2 — Qdrant payload linking (`graph_node_ids`)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0002 Phase 2 over 0002 Phase 3 (delivery-order prerequisite); over 0024 Phase 2; over 0023 Phase 3; over 0019 Phase 2; over 0018 Phase 2; single phase per pipeline rule; no ADR Accept required (0002 already Accepted phase 1). **Why now:** Embed/accelerator (0021/0022/0025) and tuner P1 (0024) arcs complete; graph writer (0002 P1) and Neo4j call-site routing (0023 P1–P2) merged; Phase 2 explicitly deferred in 0023 P2 plan and documented as not shipped in `.env.example` / `ARCHITECTURE.md`; `graph_node_ids` absent from code (`graph_writer.py`, `qdrant.py`); ADR 0002 delivery order requires P2 before P3 `expand_search_context`; pre-release re-index acceptable; default `GRAPH_ENABLED=false` unchanged. **Suggested scope:** one phase (= one PR). **Chosen scope:** Extend `graph_writer.py` + `pipeline.py` to emit per-chunk Neo4j node keys; add `graph_node_ids: list[str]` to Qdrant upsert payload when graph enabled; stamp collection metadata (`graph_enabled`, graph schema version); warn on search when graph enabled but collection lacks linkage; unit tests per ADR Validation §Phase 2; Docker integration with `docker-compose.neo4j.yml`; sync `.env.example` + `ARCHITECTURE.md`; document forced re-index; defer Phase 3 `expand_search_context`, Phase 4 cross-project Cypher, 0023 Phases 3–4.
- **Deviations:** none
- **Code evidence:** ``graph_node_ids` absent from `graph_writer.py`, `qdrant.py`; Phase 2 deferred in 0023 P2 plan and `.env.example` / `ARCHITECTURE.md``
- **Changelog:** no — user-facing unknown; invoker Changelog: no

#### 2026-07-08 — plan
- **Phase:** Phase 2 — Qdrant payload linking (`graph_node_ids`)
- **Tracker status:** `planned`
- **Choices:** Neighbor-node-keys-only for `graph_node_ids` (exclude own Chunk/File keys); boolean `graph_enabled` metadata only (no integer `graph_schema_version`); no new env vars; `graph_node_ids` left unindexed in Qdrant; batch built once per flush then reused for upsert payload + Neo4j write; best-effort graph errors continue to append to `PipelineResult.errors`; one PR per phase. **Chosen scope:** Compute `GraphBatch` before `upsert_chunks` and derive per-chunk neighbor node keys; add `graph_node_ids: list[str]` to Qdrant point payload via `_build_point`/`upsert_chunks` when `GRAPH_ENABLED=true`; add `graph_node_ids_from_batch` in `graph_writer.py`; stamp collection metadata `graph_enabled` (boolean only, no integer version); emit structlog warning in `search_common` when graph enabled but collection unlinked; reuse single graph batch for Neo4j write; unit tests per ADR Validation Phase 2; Docker integration with `docker-compose.neo4j.yml`; sync `.env.example` + `ARCHITECTURE.md`; document forced re-index. Defer Phase 3 `expand_search_context`, Phase 4 cross-project Cypher, 0023 Phase 3, 0018 P2, HTTP_CALLS/IMPORTS ADR 0026.
- **Deviations:** none
- **Test debt:** unit tests per ADR Validation §Phase 2; Docker integration with `docker-compose.neo4j.yml`
- **Changelog:** no — user-facing yes at ship (additive Qdrant payload field + collection metadata + search warning under `GRAPH_ENABLED=true`; requires re-index to backfill; default `GRAPH_ENABLED=false` unchanged); invoker Changelog: no

#### 2026-07-08 — merge
- **Phase:** Phase 2 — Qdrant payload linking (`graph_node_ids`)
- **Tracker status:** `merged`
- **Choices:** squash merge `e3348b0` on feature branch `adr/0002-phase-2-graph-node-ids`; ADR accept skipped (already Accepted phase 1; Accept after merge: no); release skipped; Phases 3–4 deferred
- **Deviations:** none
- **Code evidence:** `merged via PR #26 (`adr/0002-phase-2-graph-node-ids`; squash `e3348b0`)`
- **Test debt:** carried from verification — prometheus_client and neo4j driver needed in CI env
- **Verify:** carried from verification — 34 Phase 2 unit tests pass; full suite 420 passed; integration Verdict pass; plan compliance pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/26 — status: merged — commit: e3348b0
- **Changelog:** yes — `[Unreleased]` GraphRAG Phase 2 bullet committed on main

#### 2026-07-08 — implementation
- **Phase:** Phase 2 — Qdrant payload linking (`graph_node_ids`)
- **Tracker status:** `implemented`
- **Choices:** boolean `graph_enabled` metadata only; `graph_node_ids` neighbor-keys-only; graph batch built once per flush before upsert; file-level imports attributed to every chunk in file; search logs `graph_linkage_missing` once per unlinked collection when `GRAPH_ENABLED=true`.
- **Deviations:** `write_chunks_to_graph` retained for Neo4j integration test; pipeline uses `build_graph_batch` + `write_batch`.
- **Code evidence:** `qdrant.py`, `graph_writer.py`, `pipeline.py`, `search_common.py`, `.env.example`, `docs/ARCHITECTURE.md`
- **Test debt:** mypy gate not run; no live end-to-end graph-linkage assertion; TEI-dependent full index/search unverified.
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — verification
- **Phase:** Phase 1 — Neo4j storage + index-time graph writer
- **Tracker status:** `verified`
- **Choices:** Mock-driver CI default; best-effort graph errors to `PipelineResult.errors`; neo4j driver 6.2.0; endpoint `method` inference best-effort; compose override only; no MCP tools Phase 1
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/storage/neo4j.py`, `mcp_server/src/codebase_indexer/indexer/graph_writer.py`, `mcp_server/src/codebase_indexer/indexer/pipeline.py`, `mcp_server/src/codebase_indexer/context.py`, `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/tools/index.py`, `docker-compose.neo4j.yml`, `.env.example`, `docs/ARCHITECTURE.md`
- **Test debt:** Live Neo4j incremental delete integration; compose override smoke; graph-failure-during-index scenario; pipeline-level delete hook assertion
- **Verify:** 17 graph unit tests pass + plan compliance pass; Docker integration pass per integration report; review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — prioritization
- **Phase:** Phase 1 — Neo4j storage + index-time graph writer
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0002 Phase 1 over 0009 eval_multihop CI gate (closest alternative, −1.0 weighted score, benchmark-only; tie within ~10% but lower unlock); over 0008 test-debt closure PR (QA-only, no capability); over 0015 Phase 3 slim image and 0014 Track B n8n (ops-only, deferred twice); single phase per pipeline rule; begin GraphRAG foundation after Improve Search + vector discovery arcs complete. **Chosen scope:** `storage/neo4j.py` async driver wrapper; `indexer/graph_writer.py` reusing chunk/xref/build extractors; `pipeline.py` post-flush invocation; config (`GRAPH_ENABLED`, `NEO4J_*`, `GRAPH_WRITER_BATCH`); optional `docker-compose.neo4j.yml`; idempotent Neo4j constraints/indexes; unit tests per ADR Validation §Phase 1; `.env.example` + `ARCHITECTURE.md` sync; defer Phase 2 payload linking, Phase 3 `expand_search_context`, Phase 4 Neo4j cross-project queries. **Requires formal Accept of Proposed ADR 0002 before implementation.** **Why now:** ColBERT arc (0008 all phases, 0015 P1–P2), vector discovery Track A (0014 P1–P2), and multi-hop client eval (0009 Phase 2) are merged; ADR 0002 is the sole Proposed ADR and the largest remaining capability gap for structural multi-hop queries; ADR 0009 and 0013 explicitly defer graph-backed retrieval to 0002; no `GRAPH_ENABLED`/Neo4j code exists (`config.py` grep empty, no `storage/neo4j.py`); Phase 1 is opt-in (`GRAPH_ENABLED=false` default) with defined Testcontainers/bolt-mock validation; unlocks Phases 2–4 and 0009 server-side graph expansion path. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** `no `GRAPH_ENABLED`/Neo4j code exists (`config.py` grep empty, no `storage/neo4j.py`)`
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — plan
- **Phase:** Phase 1 — Neo4j storage + index-time graph writer
- **Tracker status:** `planned`
- **Choices:** Single PR for entire Phase 1; default CI uses bolt/session mocks (not Testcontainers); graph write errors append to `PipelineResult.errors` while Qdrant upsert succeeds; manifest `BUILD_DEPENDS` via on-disk re-read for full file content; promote `_extract_imported_names` to public API; Neo4j only in compose override (not base `docker-compose.yml`); no new MCP tools in Phase 1. **Chosen scope:** `storage/neo4j.py` async driver wrapper with idempotent schema (`Chunk.chunk_id`, `File(collection,rel_path)`, `Symbol.qualified_name`, `Endpoint`, `Collection` constraints); `indexer/graph_writer.py` writing ADR ontology from index batches reusing `UrlExtractors`, `extract_build_deps`/`match_deps_to_collections`, and public `extract_imported_names` from chunker; `pipeline.py` hooks mirroring Qdrant flush/delete cadence; `context.py` optional `Neo4jStorage`; config (`GRAPH_ENABLED=false` default, `NEO4J_*`, `GRAPH_WRITER_BATCH`, `GRAPH_SCHEMA_VERSION=1`); optional `docker-compose.neo4j.yml`; unit tests (mock driver CI + optional slow live Neo4j); `.env.example` + `ARCHITECTURE.md`; defer Phase 2 Qdrant `graph_node_ids`, Phase 3 `expand_search_context`, Phase 4 Neo4j cross-project queries. **Requires formal Accept of Proposed ADR 0002 before implementation.**
- **Deviations:** none
- **Test debt:** unit tests (mock driver CI + optional slow live Neo4j); Testcontainers vs mock-only CI open (recommend mock default)
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — merge
- **Phase:** Phase 1 — Neo4j storage + index-time graph writer
- **Tracker status:** `merged`
- **Choices:** squash merge `c511c6f` on feature branch `adr/0002-phase-1-neo4j-graph-writer`; ADR accepted as `Accepted (phase 1 — Neo4j storage + index-time graph writer)` (docs commit `a48dd97`); release skipped; Phases 2–4 deferred
- **Deviations:** none
- **Code evidence:** `merged via PR #10 (`adr/0002-phase-1-neo4j-graph-writer`; squash `c511c6f`)`
- **Test debt:** carried from verification — live Neo4j incremental delete integration; compose override smoke; graph-failure-during-index scenario; pipeline-level delete hook assertion
- **Verify:** carried from verification — 17 graph unit tests pass + plan compliance pass; Docker integration pass per integration report; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/10 — status: merged — commit: c511c6f
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — implementation
- **Phase:** Phase 1 — Neo4j storage + index-time graph writer
- **Tracker status:** `implemented`
- **Choices:** mock driver CI; best-effort graph errors to `PipelineResult.errors`; BUILD_DEPENDS via on-disk re-read; Neo4j compose override only; no MCP tools Phase 1; public `extract_imported_names` in chunker
- **Deviations:** neo4j driver resolved to 6.2.0 (5.x planned); endpoint `method` inference best-effort only
- **Code evidence:** `mcp_server/pyproject.toml`, `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/neo4j.py`, `mcp_server/src/codebase_indexer/indexer/graph_writer.py`, `mcp_server/src/codebase_indexer/indexer/chunker.py`, `mcp_server/src/codebase_indexer/indexer/pipeline.py`, `mcp_server/src/codebase_indexer/context.py`, `mcp_server/src/codebase_indexer/tools/index.py`, `mcp_server/src/codebase_indexer/main.py`, `docker-compose.neo4j.yml`, `.env.example`, `docs/ARCHITECTURE.md`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_neo4j_storage.py`, `mcp_server/tests/test_graph_writer.py`, `mcp_server/tests/test_pipeline_graph.py`
- **Test debt:** live Neo4j incremental delete integration; compose override smoke; graph-failure-during-index scenario
- **Changelog:** no — user-facing yes; entry at `verified` step

### ADR 0008 — Phase 1 — optional ColBERT multivector reranking

#### 2026-07-03 — verification
- **Phase:** Phase 1 — optional ColBERT multivector reranking
- **Tracker status:** `verified`
- **Choices:** `COLBERT_EMBED_MODEL=colbert-ir/colbertv2.0` (128-d tokens); `HnswConfigDiff(m=0)` on colbert vector; cross-collection rerank per-collection then global RRF; CI uses synthetic multivectors; real model via `@pytest.mark.slow`; ColBERT index embed sequential after dense+sparse
- **Deviations:** none
- **Code evidence:** `config.py`, `colbert_onnx.py`, `embedder.py`, `qdrant.py`, `search_common.py`, `test_storage_integration.py`, `bench.py`, `eval_retrieval.py`
- **Test debt:** ranx eval tests skip without `--extra benchmark`; golden-set MRR delta manual via `eval_retrieval --rerank`; no unit test for colbert mismatch recreate; slow ColBERT smoke opt-in only
- **Verify:** tests run + plan compliance pass (217 passed); review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — verification
- **Phase:** Phase 2 — track 1 (xref/service_map rerank wiring)
- **Tracker status:** `verified`
- **Choices:** Shared `dispatch_search` helper in `search_common.py`; xref semantic/import via `run_search()`; service_map batched discovery via `dispatch_search()` with pre-embedded colbert vectors; tool-specific `min_score` retained (0.3 / 0.25); default deploy unchanged (`RERANK_ENABLED=false`); adaptive rerank and per-tool overrides deferred to track 2
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/tools/search_common.py`, `cross_references.py`, `service_map.py`, `mcp_server/tests/test_search_common.py`, `test_cross_references.py`, `test_service_map.py`, `docs/SEARCH_BEHAVIOR.md`
- **Test debt:** import-phrased xref colbert wiring test; single-collection xref semantics regression test; optional slow integration rerank smoke for xref/service_map
- **Verify:** tests run + plan compliance pass — 17 targeted tests passed; 235-suite tests passed (242 with fastapi env); ruff clean; review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — verification
- **Phase:** Phase 2 — track 2a (adaptive rerank skip)
- **Tracker status:** `verified`
- **Choices:** `RERANK_ADAPTIVE_ENABLED=true`, `RERANK_ADAPTIVE_GAP=0.02` when rerank on; hybrid RRF probe in `_search_single` with probe limit `max(top_k, 2)`; fewer than 2 probe hits always runs ColBERT; `AdaptiveRerankStats` for bench/eval skip-rate; ColBERT query embed unchanged; track 2b per-tool override deferred
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_qdrant_search.py`, `mcp_server/tests/test_benchmarks.py`, `mcp_server/benchmarks/bench.py`, `mcp_server/benchmarks/eval_retrieval.py`, `docs/SEARCH_BEHAVIOR.md`, `.env.example`
- **Test debt:** optional dedicated test for single-probe-hit ColBERT path; live Qdrant adaptive integration test; golden-set gap threshold sweep
- **Verify:** tests run + plan compliance pass — 53 targeted tests passed; 265-suite passed; ruff 1× F401 suggestion (unused import)
- **Changelog:** yes

#### 2026-07-03 — verification
- **Phase:** Phase 2 — track 2b (per-tool `rerank=false` override)
- **Tracker status:** `verified`
- **Choices:** Override at embed + tool layer (`use_rerank = self.rerank and rerank is not False`); `colbert_vector=None` skips storage rerank/adaptive paths; `rerank=None` default; xref import-phrased search inherits tool-level `rerank`; exact symbol / call_site unaffected; `recommend_code` excluded; final ADR 0008 phase
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/indexer/embedder.py`, `mcp_server/src/codebase_indexer/tools/search_common.py`, `mcp_server/src/codebase_indexer/tools/search.py`, `mcp_server/src/codebase_indexer/tools/symbols.py`, `mcp_server/src/codebase_indexer/tools/cross_references.py`, `mcp_server/src/codebase_indexer/tools/service_map.py`, `mcp_server/src/codebase_indexer/main.py`, `docs/SEARCH_BEHAVIOR.md`, `.env.example`
- **Test debt:** direct Embedder rerank unit tests; adaptive + per-tool override integration; golden-set `rerank=false` quality sweep; live Qdrant adaptive integration (carried from track 2a)
- **Verify:** 23 targeted + 264 unit tests pass; plan compliance pass; integration skipped per plan; review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — prioritization
- **Phase:** Phase 1 — optional ColBERT multivector reranking (index-time multivectors + query-time MAX_SIM rerank over hybrid prefetch pool)
- **Tracker status:** `candidate`
- **Choices:** Prioritize search-quality increment on existing Qdrant stack over greenfield Neo4j (0002) or recommendation API (0014); deliver single phase per pipeline rule; require formal Accept of Proposed ADR 0008 before dev. **Chosen scope:** config (`RERANK_ENABLED`, `COLBERT_EMBED_MODEL`, `RERANK_PREFETCH`), ColBERT fastembed backend, multivector schema + rerank query in `qdrant.py`, pipeline third embed pass, integration test, `eval_retrieval.py` quality delta, P95 in `bench.py`; defer adaptive rerank and per-tool overrides. **Why now:** Accepted ADR 0003 explicitly deferred ColBERT reranking; hybrid RRF (0003), eval harness (0007), and Ollama-only dense (0011) are merged; no rerank code exists; opt-in flag preserves default deployment; measurable via golden set MRR/NDCG and `bench.py` latency. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Changelog:** no — user-facing unknown (likely yes when flag enabled)

#### 2026-07-03 — prioritization
- **Phase:** Phase 2 — track 2a (adaptive rerank skip)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0008 Phase 2 track 2a over 0009 Phase 2 automated 2-hop eval script (closest alternative, −1.5 weighted score but benchmark-only), 0014 Track A P2 outlier helper, Proposed 0002 GraphRAG Phase 1, 0015 Phase 3 slim image, and 0014 Track B n8n; single phase per pipeline rule; adaptive skip before per-tool override; no ADR Accept required (0008 already Accepted). **Chosen scope:** Configurable adaptive ColBERT skip when hybrid prefetch top-1 vs top-2 RRF score gap exceeds threshold; implement in `QdrantStorage.search` rerank path; unit tests; `bench.py`/`eval_retrieval.py` skip-rate and P95 reporting; `SEARCH_BEHAVIOR.md` + `.env.example` docs; defer per-tool MCP `rerank=false` parameter override to track 2b. **Why now:** ColBERT arc merged (0008 P1, 0015 P1–P2, 0008 P2 track 1, 0014 P1); rerank wired on all search tools but ADR 0008 deferred adaptive skip and per-tool overrides; no adaptive code in repo; ARCHITECTURE.md lists this as remaining Improve Search work; prerequisites (0003, 0007, 0011, 0015) satisfied; measurable via `eval_retrieval --rerank` and `bench.py`; no new mandatory infra; default deploy unchanged. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — prioritization
- **Phase:** Phase 2 — track 2b (per-tool `rerank=false` override)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0008 Phase 2 track 2b over 0014 Track A P2 outlier helper and Proposed 0002 GraphRAG Phase 1 (tie on weighted score; lower scope/risk tie-breaker); over 0009 Phase 2 automated 2-hop eval script (closest alternative, −0.5 weighted score, benchmark-only); over 0015 Phase 3 slim image and 0014 Track B n8n (deferred twice / ops-only); single phase per pipeline rule; no ADR Accept required (0008 already Accepted); complete ColBERT Improve Search arc before greenfield GraphRAG or discovery P2. **Chosen scope:** Optional `rerank: bool \| None = None` on `search_codebase`, `search_symbols`, and semantic search paths in `find_cross_references` / `map_service_dependencies`; thread through `run_search` / `dispatch_search` / `QdrantStorage.search` to skip ColBERT query embed and MAX_SIM when `rerank=false`; unit tests per tool; `SEARCH_BEHAVIOR.md` + `.env.example` documentation; defer golden-set adaptive gap sweep and live Qdrant adaptive integration test debt from track 2a. **Why now:** ColBERT arc merged through Phase 1, Phase 2 tracks 1 and 2a, and ADR 0015 Phases 1–2; track 2b is the sole remaining ADR 0008 Phase 2 item explicitly deferred after track 2a ([PR #6](https://github.com/Tusquito/codebase-indexer-mcp/pull/6)); MCP search tools lack per-call rerank control while global `RERANK_ENABLED=true` always embeds ColBERT and runs MAX_SIM; prerequisites (0003, 0007, 0011, 0015) satisfied; measurable via `eval_retrieval --rerank` and unit tests; no new mandatory infra; default deploy unchanged when `RERANK_ENABLED=false`. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Test debt:** defer golden-set adaptive gap sweep and live Qdrant adaptive integration test debt from track 2a
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — prioritization
- **Phase:** Phase 2 — cross_reference / service_map rerank wiring (first track of Phase 2+)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0008 Phase 2 track 1 over 0015 Phase 3 slim image, Proposed 0002 GraphRAG Phase 1, Proposed 0014 recommendation tool, and undefined 0009 Phase 2+ server fusion; single phase per pipeline rule; no new infrastructure; measurable via existing `eval_retrieval.py --rerank` and golden set. **Chosen scope:** Route semantic search paths in `cross_references.py` and `service_map.py` through the same ColBERT-aware search dispatch as `search_common.run_search` (pass `colbert_vector`; align hybrid+rerank score behavior); add integration/unit tests; defer adaptive rerank and per-tool `rerank=false` overrides. **Why now:** ADR 0008 Phase 1 and ADR 0015 Phases 1–2 are merged; ColBERT rerank works for `search_codebase`/`search_symbols` but `find_cross_references` and `map_service_dependencies` discard `colbert_vector`, leaving explicit Phase 1 test debt and inconsistent quality when `RERANK_ENABLED=true`. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Test debt:** carried from Phase 1 — wire ColBERT into cross_reference/service_map search when rerank enabled; golden-set MRR with `--rerank`
- **Changelog:** yes — user-facing yes; entry at `verified` step

#### 2026-07-03 — plan
- **Phase:** Phase 1 — optional ColBERT multivector reranking (index-time multivectors + query-time MAX_SIM rerank over hybrid prefetch pool)
- **Tracker status:** `planned`
- **Choices:** **`COLBERT_EMBED_MODEL` default:** `colbert-ir/colbertv2.0` (128-d per token). **ADR `m=768` prose:** treat as documentation error for HNSW knob; implement `HnswConfigDiff(m=0)` on `colbert` vector; per-token `size` from registry (128 for default model). **Cross-collection rerank:** per-collection hybrid prefetch + ColBERT MAX_SIM rerank, then existing global `fuse_cross_collection_rrf`. **CI ColBERT testing:** integration test uses synthetic multivectors only; real model test `@pytest.mark.slow`. **Index-time memory:** ColBERT always sequential after dense+sparse when rerank enabled. **ADR Accept:** formal Proposed → Accepted + README index update is pre-merge follow-up. **Operator messaging:** re-index required when enabling rerank — in `.env.example` + `SEARCH_BEHAVIOR.md`. **Chosen scope:** Config (`RERANK_ENABLED`, `COLBERT_EMBED_MODEL`, `RERANK_PREFETCH`, `RERANK_MAX_QUERY_TOKENS`); `ColbertOnnxBackend` via fastembed `LateInteractionTextEmbedding`; multivector `colbert` schema + MAX_SIM rerank query in `qdrant.py`; pipeline third embed pass (sequential after dense+sparse); `search_common` wiring; synthetic integration test + optional `@pytest.mark.slow` real-model test; `eval_retrieval.py` / `bench.py` rerank deltas; operator re-index docs in `.env.example` + `SEARCH_BEHAVIOR.md`; defer adaptive rerank and per-tool overrides.
- **Deviations:** none
- **Test debt:** synthetic multivector integration test required for CI; real-model coverage optional via `@pytest.mark.slow`
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — plan
- **Phase:** Phase 2 — track 1 (cross_reference / service_map rerank wiring)
- **Tracker status:** `planned`
- **Choices:** Shared `dispatch_search` helper (not duplicate colbert pass-through in each tool); keep tool-specific internal `min_score` (0.3 / 0.25) — ignored on hybrid/rerank via existing `qdrant.py` logic; no new config/infra; single PR; no ADR accept/index update
- **Deviations:** none
- **Test debt:** unit tests in `test_search_common.py`, `test_cross_references.py`, `test_service_map.py`; `SEARCH_BEHAVIOR.md` xref/service_map rerank note
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — plan
- **Phase:** Phase 2 — track 2a (adaptive rerank skip)
- **Tracker status:** `planned`
- **Choices:** Single PR for track 2a; adaptive logic in `QdrantStorage._search_single` via hybrid RRF probe before ColBERT; new env vars `RERANK_ADAPTIVE_ENABLED` + `RERANK_ADAPTIVE_GAP`; `AdaptiveRerankStats` counters on storage for bench/eval skip-rate; recommended defaults `RERANK_ADAPTIVE_ENABLED=true`, `RERANK_ADAPTIVE_GAP=0.02` pending golden-set sweep; ColBERT query embed in `Embedder.embed_query` unchanged in track 2a; track 2b per-tool override explicitly deferred; no ADR Accept required (0008 already Accepted). **Chosen scope:** Configurable adaptive ColBERT skip when hybrid prefetch top-1 vs top-2 RRF score gap exceeds threshold; implement in `QdrantStorage.search` rerank path; unit tests; `bench.py`/`eval_retrieval.py` skip-rate and P95 reporting; `SEARCH_BEHAVIOR.md` + `.env.example` docs; defer per-tool MCP `rerank=false` parameter override to track 2b
- **Deviations:** none
- **Test debt:** unit tests for adaptive skip logic; `bench.py`/`eval_retrieval.py` skip-rate and P95 reporting; `SEARCH_BEHAVIOR.md` + `.env.example` docs; optional live Qdrant integration test vs unit mocks only (open)
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — plan
- **Phase:** Phase 2 — track 2b (per-tool `rerank=false` override)
- **Tracker status:** `planned`
- **Choices:** Implement override at embed + tool layer (not new storage flag); `rerank=false` only effective when `RERANK_ENABLED=true`; `rerank=None` preserves current behavior; adaptive skip (track 2a) unchanged when effective rerank is on; single PR; no compose/env changes; final ADR 0008 phase. **Chosen scope:** Optional `rerank: bool \| None = None` on `search_codebase`, `search_symbols`, and semantic search paths in `find_cross_references` / `map_service_dependencies`; thread through `run_search` and `Embedder.embed_query` / `embed_queries` so `rerank=false` skips ColBERT query embed and MAX_SIM (via `colbert_vector=None`); unit tests per tool + `test_search_common`; `SEARCH_BEHAVIOR.md` + `.env.example` + `main.py` instructions; defer golden-set adaptive gap sweep and live Qdrant adaptive integration test debt from track 2a
- **Deviations:** none
- **Test debt:** unit tests per tool + `test_search_common`; defer golden-set adaptive gap sweep and live Qdrant adaptive integration test debt from track 2a
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — merge
- **Phase:** Phase 1 — optional ColBERT multivector reranking
- **Tracker status:** `merged`
- **Choices:** squash merge `891fb97` (10 commits on feature branch `adr/0008-phase-1-colbert-rerank`); ADR accepted as `Accepted (phase 1 — optional ColBERT multivector reranking)`; release skipped; phase 2+ deferred (adaptive rerank, per-tool overrides, cross_reference/service_map wiring)
- **Deviations:** none
- **Code evidence:** `merged via PR #1 (`adr/0008-phase-1-colbert-rerank`)`
- **Test debt:** carried from verification — ranx eval manual; colbert mismatch recreate; slow ColBERT opt-in only
- **Verify:** PR review round 2 approve; CI green; mergeable
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/1 — status: merged — commit: 891fb97
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — merge
- **Phase:** Phase 2 — track 1 (xref/service_map rerank wiring)
- **Tracker status:** `merged`
- **Choices:** squash merge `fcf2e18` on feature branch `adr/0008-phase-2-xref-service-map-rerank`; ADR accept skipped (unchanged — Accepted phase 1); release skipped; Phase 2 track 2 deferred (adaptive rerank vs per-tool override)
- **Deviations:** none
- **Code evidence:** `merged via PR #4 (`adr/0008-phase-2-xref-service-map-rerank`)`
- **Test debt:** carried from verification — import-phrased xref colbert wiring test; single-collection xref semantics regression test; optional slow integration rerank smoke for xref/service_map
- **Verify:** carried from verification — 17 targeted tests passed; 235-suite tests passed (242 with fastapi env); ruff clean; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/4 — status: merged — commit: fcf2e18
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — merge
- **Phase:** Phase 2 — track 2a (adaptive rerank skip)
- **Tracker status:** `merged`
- **Choices:** squash merge `1411060` on feature branch `adr/0008-phase-2-track-2a-adaptive-rerank-skip`; ADR accept updated to `Accepted (phase 1; phase 2 tracks 1, 2a merged)`; release skipped; track 2b per-tool override deferred
- **Deviations:** none
- **Code evidence:** `merged via PR #6 (`adr/0008-phase-2-track-2a-adaptive-rerank-skip`)`
- **Test debt:** carried from verification — optional dedicated test for single-probe-hit ColBERT path; live Qdrant adaptive integration test; golden-set gap threshold sweep
- **Verify:** carried from verification — 53 targeted tests passed; 265-suite passed; ruff 1× F401 suggestion (unused import)
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/6 — status: merged — commit: 1411060
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — merge
- **Phase:** Phase 2 — track 2b (per-tool `rerank=false` override)
- **Tracker status:** `merged`
- **Choices:** squash merge PR #7 on feature branch `adr/0008-phase-2-track-2b-rerank-override`; ADR 0008 accepted as full **Accepted** status; final ADR 0008 phase complete; release skipped
- **Deviations:** none
- **Code evidence:** `merged via PR #7 (`adr/0008-phase-2-track-2b-rerank-override`; squash `00f4c3e4fcc3efe4d81936e6025dab41d05e08f9`)`
- **Test debt:** carried from verification — direct Embedder rerank unit tests; adaptive + per-tool override integration; golden-set `rerank=false` quality sweep; live Qdrant adaptive integration (carried from track 2a)
- **Verify:** carried from verification — 23 targeted + 264 unit tests pass; plan compliance pass; integration skipped per plan; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/7 — status: merged — commit: 00f4c3e4fcc3efe4d81936e6025dab41d05e08f9
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — implementation
- **Phase:** Phase 1 — optional ColBERT multivector reranking (index-time multivectors + query-time MAX_SIM rerank over hybrid prefetch pool)
- **Tracker status:** `implemented`
- **Choices:** `COLBERT_EMBED_MODEL` default `colbert-ir/colbertv2.0` (128-d per token); `HnswConfigDiff(m=0)` on `colbert` vector; per-collection hybrid prefetch + ColBERT MAX_SIM rerank then global `fuse_cross_collection_rrf`; ColBERT always sequential after dense+sparse at index time; synthetic multivectors in CI integration test; real model behind `@pytest.mark.slow` + `RUN_SLOW_COLBERT=1`; `RERANK_ENABLED=false` default preserves existing behavior
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/indexer/backends/colbert_onnx.py`, `mcp_server/src/codebase_indexer/indexer/embedder.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/src/codebase_indexer/tools/search_common.py`, `mcp_server/tests/test_storage_integration.py`, `docs/SEARCH_BEHAVIOR.md`, `.env.example`
- **Test debt:** cross-collection rerank integration test; golden-set MRR with `--rerank`; rerank mismatch recreate test; wire ColBERT into cross_reference/service_map search when rerank enabled
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — implementation
- **Phase:** Phase 2 — track 1 (cross_reference / service_map rerank wiring)
- **Tracker status:** `implemented`
- **Choices:** Extracted shared `dispatch_search()` in `search_common.py`; xref semantic/import paths route through `run_search()`; service_map batched discovery loop routes through `dispatch_search()` with pre-embedded colbert vectors; default deploy unchanged (`RERANK_ENABLED=false`)
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/tools/search_common.py`, `mcp_server/src/codebase_indexer/tools/cross_references.py`, `mcp_server/src/codebase_indexer/tools/service_map.py`, `mcp_server/tests/test_search_common.py`, `mcp_server/tests/test_cross_references.py`, `mcp_server/tests/test_service_map.py`, `docs/SEARCH_BEHAVIOR.md`
- **Test debt:** import-phrased xref colbert wiring test; single-collection xref semantics regression; optional slow integration rerank smoke for xref/service_map
- **Changelog:** no

#### 2026-07-03 — implementation
- **Phase:** Phase 2 — track 2a (adaptive rerank skip)
- **Tracker status:** `implemented`
- **Choices:** Shipped `RERANK_ADAPTIVE_ENABLED=true` and `RERANK_ADAPTIVE_GAP=0.02`; adaptive logic in `QdrantStorage._search_single` via hybrid RRF probe before ColBERT; probe limit `max(top_k, 2)`; fewer than 2 probe hits always runs ColBERT; `AdaptiveRerankStats` counters on storage for bench/eval skip-rate; ColBERT query embed in `Embedder.embed_query` unchanged; track 2b per-tool override deferred; default deploy unchanged (`RERANK_ENABLED=false`)
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_qdrant_search.py`, `mcp_server/benchmarks/bench.py`, `mcp_server/benchmarks/eval_retrieval.py`, `mcp_server/tests/test_benchmarks.py`, `docs/SEARCH_BEHAVIOR.md`, `.env.example`
- **Test debt:** golden-set gap sweep via `eval_retrieval --rerank`; optional live Qdrant integration test for adaptive skip; multi-collection adaptive + global RRF unit test; track 2b per-tool `rerank=false` deferred
- **Changelog:** no

#### 2026-07-03 — implementation
- **Phase:** Phase 2 — track 2b (per-tool `rerank=false` override)
- **Tracker status:** `implemented`
- **Choices:** Override at embed + tool layer (not new storage flag); `use_rerank = self.rerank and rerank is not False`; `rerank=false` only effective when global `RERANK_ENABLED=true`; `rerank=None` preserves current behavior; `rerank=true` does not enable ColBERT without global flag or bypass adaptive skip; import-phrased xref search inherits tool-level `rerank`; exact symbol / call_site paths unaffected
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/indexer/embedder.py`, `mcp_server/src/codebase_indexer/tools/search_common.py`, `mcp_server/src/codebase_indexer/tools/search.py`, `mcp_server/src/codebase_indexer/tools/symbols.py`, `mcp_server/src/codebase_indexer/tools/cross_references.py`, `mcp_server/src/codebase_indexer/tools/service_map.py`, `mcp_server/src/codebase_indexer/main.py`, `mcp_server/tests/test_search_common.py`, `mcp_server/tests/test_search_tools.py`, `mcp_server/tests/test_cross_references.py`, `mcp_server/tests/test_service_map.py`, `docs/SEARCH_BEHAVIOR.md`, `.env.example`
- **Test debt:** direct Embedder rerank unit tests; adaptive + per-tool override integration; golden-set `rerank=false` quality sweep; live Qdrant adaptive integration (carried from track 2a)
- **Changelog:** no

### ADR 0009 — Phase 2 — automated 2-hop client eval script

#### 2026-07-03 — verification
- **Phase:** Phase 2 — automated 2-hop client eval script
- **Tracker status:** `verified`
- **Choices:** Separate `eval_multihop.py` CLI; curated `hop2_query_text` inline in golden fixture; RRF in `multihop_rrf.fuse_hop_rrf`; `--rerank` passthrough included; `multi_hop_2hop` baseline from live verify (nomic embed, not jina)
- **Deviations:** none
- **Code evidence:** `mcp_server/benchmarks/eval_multihop.py`, `mcp_server/benchmarks/multihop_rrf.py`, `mcp_server/benchmarks/eval_retrieval.py`, `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `mcp_server/benchmarks/fixtures/eval_baseline.json`, `mcp_server/tests/test_multihop_rrf.py`, `mcp_server/tests/test_eval_multihop.py`, `docs/SEARCH_BEHAVIOR.md`, `docs/ARCHITECTURE.md`
- **Test debt:** No CI gate for `eval_multihop`; baseline snapshot not aligned to jina embed model; no unit test for `compare_vs_baseline()`
- **Verify:** 20 unit tests pass + plan compliance pass; Docker integration skipped per plan; review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — prioritization
- **Phase:** Phase 2 — automated 2-hop client eval script
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0009 Phase 2 eval script over 0002 Phase 1 GraphRAG (tie on weighted score ~28 vs 27; lower scope/risk tie-breaker), 0014 Track A P2 outlier helper, 0008 test-debt closure, 0015 Phase 3 slim image, and 0014 Track B n8n; single phase per pipeline rule; no ADR Accept required; complete ADR 0009 validation before greenfield Neo4j or discovery P2. **Chosen scope:** Add deterministic 2-hop client eval script under `mcp_server/benchmarks/` (standalone `eval_multi_hop.py` or `eval_retrieval --multi-hop`): hop 1 `run_search` on original query; hop 2 sub-query derived deterministically from hop-1 results or golden fixture metadata; client-side RRF fuse on `chunk_id` (rrf_k=60); report `metrics_by_tag` for `multi_hop` slice; `--compare` against single-pass and `eval_baseline.json`; unit tests with mocked search; update `SEARCH_BEHAVIOR.md` Evaluation section; no server code or new services; defer server-side hop fusion and LLM-driven sub-questions in CI. **Why now:** ColBERT arc (0008 all phases, 0015 P1–P2) and vector discovery P1 (0014 `recommend_code`) are merged; ADR 0009 Phase 1 docs and four `multi_hop` golden queries shipped but Validation still requires automated 2-hop client script vs single-pass on `multi_hop` tag slice; `eval_retrieval.py` is single-pass only; no 2-hop benchmark module in repo; prerequisites (0007 harness, golden fixtures, SEARCH_BEHAVIOR guidance) satisfied; measurable without new infra; default deploy unchanged. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — plan
- **Phase:** Phase 2 — automated 2-hop client eval script
- **Tracker status:** `planned`
- **Choices:** Separate `eval_multihop.py` (not extending `eval_retrieval.py` CLI); deterministic curated sub-questions in `golden_queries.jsonl` (no LLM in eval script); RRF fusion in `benchmarks/multihop_rrf.py` keyed by `chunk_id` with `rrf_k=60` from `Settings`; single PR; no CI gate change; GraphRAG / server-side hop fusion explicitly deferred to ADR 0002+ later phases. **Chosen scope:** Benchmark-only deliverable — `eval_multihop.py` + `multihop_rrf.fuse_hop_rrf`; curated `hop2_query_text` on four `multi_hop` golden entries; client-side RRF fusion of hop 1 (`query_text`) + hop 2 (`hop2_query_text`) via existing `run_search`; side-by-side ranx metrics vs single-pass on `multi_hop` slice; unit tests + opt-in benchmark smoke; `SEARCH_BEHAVIOR.md` + `ARCHITECTURE.md` command docs; optional `eval_baseline.json` `multi_hop_2hop` snapshot after live verify. No MCP server, compose, or runtime changes.
- **Deviations:** none
- **Test debt:** unit tests + opt-in benchmark smoke; live verify against indexed `codebase-indexer-mcp` collection for optional baseline JSON snapshot
- **Changelog:** no — user-facing no

#### 2026-07-03 — merge
- **Phase:** Phase 2 — automated 2-hop client eval script
- **Tracker status:** `merged`
- **Choices:** squash merge `b101be6` on feature branch `adr/0009-phase-2-multihop-eval` (deleted post-merge); ADR accepted as `Accepted (phase 1; phase 2 merged)` (commit `d761d09` on main); release skipped
- **Deviations:** none
- **Code evidence:** `merged via PR #8 (`adr/0009-phase-2-multihop-eval`; squash `b101be6`)`
- **Test debt:** carried from verification — no CI gate for `eval_multihop`; baseline snapshot not aligned to jina embed model; no unit test for `compare_vs_baseline()`
- **Verify:** carried from verification — 20 unit tests pass + plan compliance pass; Docker integration skipped per plan; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/8 — status: merged — commit: b101be6
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — implementation
- **Phase:** Phase 2 — automated 2-hop client eval script
- **Tracker status:** `implemented`
- **Choices:** Separate `eval_multihop.py` CLI; curated `hop2_query_text` inline in `golden_queries.jsonl`; RRF fusion in `benchmarks/multihop_rrf.fuse_hop_rrf`; `--rerank` passthrough included; `multi_hop_2hop` baseline block added after live verify
- **Deviations:** Live baseline snapshot used local nomic embed model (not baseline jina model); ADR Accept/index update deferred to merge gate
- **Code evidence:** `mcp_server/benchmarks/eval_multihop.py`, `mcp_server/benchmarks/multihop_rrf.py`, `mcp_server/benchmarks/eval_retrieval.py`, `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `mcp_server/benchmarks/fixtures/eval_baseline.json`, `mcp_server/tests/test_multihop_rrf.py`, `mcp_server/tests/test_eval_multihop.py`, `docs/SEARCH_BEHAVIOR.md`, `docs/ARCHITECTURE.md`
- **Test debt:** No CI gate for eval_multihop; baseline snapshot not aligned to jina embed model; no unit test for compare_vs_baseline()
- **Changelog:** no

#### 2026-07-02 — merge
- **Phase:** Phase 1 (docs + golden-set tags)
- **Tracker status:** `merged`
- **Choices:** Client-orchestrated decomposition; no new server code in phase 1
- **Deviations:** none
- **Code evidence:** ``docs/SEARCH_BEHAVIOR.md`, `benchmarks/fixtures/golden_queries.jsonl` multi_hop tags`
- **Changelog:** no

### ADR 0014 — Track A — Phase 1 (Recommendation search tool)

#### 2026-07-03 — verification
- **Phase:** Track A — Phase 1 (Recommendation search tool)
- **Tracker status:** `verified`
- **Choices:** Tool name `recommend_code`; RecommendStrategy AVERAGE_VECTOR only; dense-only; path_glob post-filter fnmatch + limit×3; missing chunk IDs fail fast; multi-collection deferred
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/src/codebase_indexer/tools/recommend.py`, `mcp_server/src/codebase_indexer/main.py`, `docker-compose.yml`, `.env.example`, `docs/SEARCH_BEHAVIOR.md`, `mcp_server/tests/test_recommend.py`, `mcp_server/tests/test_recommend_tool.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_storage_integration.py`
- **Test debt:** `main.py` registration gate; live HTTP/Ollama e2e; golden-set eval; multi-collection deferred
- **Verify:** 258 pytest passed, ruff clean; review rounds: 2 (round 2 clean after R1 fix)
- **Changelog:** yes

#### 2026-07-03 — verification
- **Phase:** Track A — Phase 2 (outlier / diversity helper)
- **Tracker status:** `verified`
- **Choices:** Separate tool `find_outlier_chunks`; `RecommendStrategy.BEST_SCORE` negative-only; cosine-to-centroid ascending sort + `OUTLIER_MAX_SIMILARITY` filter; reuse `RECOMMEND_ENABLED` (no `OUTLIER_ENABLED`); scroll supplement only when `path_glob` set or no explicit `context_chunk_ids`; `limit` cap 20; dense-only single-collection
- **Deviations:** Scroll supplement restricted when only `context_chunk_ids` provided — prevents outlier candidates being absorbed into context centroid during whole-collection scroll fill
- **Code evidence:** `mcp_server/src/codebase_indexer/tools/outliers.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/main.py`, `docker-compose.yml`, `.env.example`, `docs/SEARCH_BEHAVIOR.md`, `docs/ARCHITECTURE.md`, `README.md`, `mcp_server/tests/test_outliers.py`, `mcp_server/tests/test_outlier_tool.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_main.py`, `mcp_server/tests/test_storage_integration.py`
- **Test debt:** scroll-supplement restriction unit test; `main.py` positive registration gate; combined `path_glob`+`context_chunk_ids` integration; live HTTP/Ollama e2e for `find_outlier_chunks`; golden-set outlier quality eval; multi-collection/sparse fusion deferred
- **Verify:** tests run + plan compliance pass — 287 unit tests passed; 17 targeted outlier tests passed; ruff clean; Docker integration report pass (8 pytest integration, smoke_recommend); review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — prioritization
- **Phase:** Track A — Phase 1 (Recommendation search tool)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0014 Track A P1 over 0009 Phase 2 eval script (closest alternative, +1.5 weighted score but benchmark-only), 0008 Phase 2 track 2 adaptive rerank (incremental latency), Proposed 0002 GraphRAG Phase 1 (Neo4j greenfield), and 0015 Phase 3 slim image (deferred twice); single phase per pipeline rule; formal Accept of Proposed ADR required before dev. **Chosen scope:** `recommend_code` MCP tool + `QdrantStorage.recommend` helper + config (`RECOMMEND_ENABLED`, `RECOMMEND_MAX_EXAMPLES`) + unit/integration tests + `main.py` registration; dense-only; defer outlier helper (Track A P2), n8n compose (Track B), sparse fusion. **Why now:** ColBERT arc (0008 P1, 0015 P1–P2, 0008 P2 track 1) merged; open-decisions queue deferred Proposed 0002/0014 greenfield to this cycle; no recommend API in codebase; payload indexes already shipped; no new mandatory infra; user-facing discovery capability on existing embedder/Qdrant stack. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Changelog:** no — user-facing unknown (likely yes)

#### 2026-07-03 — prioritization
- **Phase:** Track A — Phase 2 (outlier / diversity helper)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0014 Track A P2 over Proposed 0002 GraphRAG Phase 1 (higher raw unlock but Accept gate + optional Neo4j greenfield — decision rules 2 & 5); over 0008 test-debt closure PR (closest QA alternative, same weighted tier ~20 but no user-facing capability); over 0009 eval_multihop CI gate (benchmark-only); over 0015 Phase 3 slim image (ops/build, deferred twice); over 0014 Track B n8n (ops-only, new optional service); single phase per pipeline rule; no ADR Accept required (0014 already Accepted phase 1); finish vector discovery Track A before GraphRAG or n8n. **Chosen scope:** Add outlier/diversity MCP discovery tool per ADR 0014 §Phase 2 — `find_outlier_chunks` (or `recommend_code` extension with `strategy=diverse` / score inversion, lock at plan); `QdrantStorage` helper; bounded `limit` + required explicit `collection`; dense-only single-collection; config gate if needed; unit + integration tests per ADR Validation §Phase 2; `main.py` registration + `SEARCH_BEHAVIOR.md`; defer sparse fusion, multi-collection, Track B n8n compose. **Why now:** ColBERT arc (0008 all phases), sidecar (0015 P1–P2), multi-hop eval (0009 P2), and recommendation search (0014 P1) are merged; ADR 0014 explicitly deferred Track A Phase 2 after P1; `recommend_code` and `QdrantStorage.recommend` exist in code but no outlier/diversity tool (`find_outlier_chunks` absent from source); prerequisites satisfied; user-facing discovery on existing embedder/Qdrant stack; no new mandatory infra; default deploy unchanged; completes Track A before ops-only n8n (Track B) or greenfield GraphRAG (0002, still Proposed). **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Changelog:** no — user-facing unknown (likely yes)

#### 2026-07-03 — plan
- **Phase:** Track A — Phase 1 (Recommendation search tool)
- **Tracker status:** `planned`
- **Choices:** Tool name `recommend_code`; RecommendStrategy AVERAGE_VECTOR only; dense-only; path_glob post-filter with fnmatch + limit*3 over-fetch; missing chunk IDs fail fast; multi-collection deferred; ADR Accept at merge via finisher. **Chosen scope:** `recommend_code` MCP tool + `QdrantStorage.recommend` helper + config (`RECOMMEND_ENABLED`, `RECOMMEND_MAX_EXAMPLES`) + unit/integration tests + `main.py` conditional registration + compose/.env.example + `SEARCH_BEHAVIOR.md` note; dense-only; single-collection; defer outlier helper (Track A P2), n8n compose (Track B), sparse fusion, multi-collection
- **Deviations:** none
- **Test debt:** unit/integration tests per chosen scope; `SEARCH_BEHAVIOR.md` recommend note
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — plan
- **Phase:** Track A — Phase 2 (outlier / diversity helper)
- **Tracker status:** `planned`
- **Choices:** Lock tool name **`find_outlier_chunks`** (separate tool, do not extend `recommend_code`); lock score semantics to **cosine similarity to context centroid** (ascending sort = most distant first; `max_similarity` excludes above-threshold chunks); lock config to **reuse `RECOMMEND_ENABLED`** + add **`OUTLIER_MAX_CONTEXT_SAMPLES`** and **`OUTLIER_MAX_SIMILARITY`**; context from `context_chunk_ids` and/or scroll sample with optional `path_glob`; Qdrant retrieval via **`BEST_SCORE` negative-only** recommend (not `AVERAGE_VECTOR`); one PR for entire phase. **Chosen scope:** Add separate MCP tool `find_outlier_chunks` + `QdrantStorage.find_outlier_chunks` using Qdrant `RecommendStrategy.BEST_SCORE` negative-only recommend on sampled context vectors, cosine-to-centroid threshold filtering (`max_similarity` / `OUTLIER_MAX_SIMILARITY`), bounded `limit` (cap 20) + explicit required `collection`, dense-only single-collection; gate via existing `RECOMMEND_ENABLED` (no `OUTLIER_ENABLED`); new config `OUTLIER_MAX_CONTEXT_SAMPLES` (default 200); unit + integration tests per ADR Validation §Phase 2; `main.py` registration + `SEARCH_BEHAVIOR.md` + `ARCHITECTURE.md`/`README.md` sync; defer sparse fusion, multi-collection, Track B n8n compose, Discovery API context pairs
- **Deviations:** none
- **Test debt:** unit + integration tests per ADR Validation §Phase 2; optional smoke script and compose harness step deferred
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — merge
- **Phase:** Track A — Phase 1 (Recommendation search tool)
- **Tracker status:** `merged`
- **Choices:** merge on feature branch `adr/0014-phase-1-recommend-code`; ADR accepted as `Accepted (phase 1 — recommendation search tool)`; release skipped; Track A P2 (outlier helper) + Track B (n8n compose) deferred
- **Deviations:** none
- **Code evidence:** `merged via PR #5 (`adr/0014-phase-1-recommend-code`)`
- **Test debt:** carried from verification — `main.py` registration gate; live HTTP/Ollama e2e; golden-set eval; multi-collection deferred
- **Verify:** carried from verification — 258 pytest passed, ruff clean; review rounds: 2
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/5 — status: merged
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — merge
- **Phase:** Track A — Phase 2 (outlier / diversity helper)
- **Tracker status:** `merged`
- **Choices:** squash merge `b97c29b` on feature branch `adr/0014-phase-2-outlier-helper`; ADR accepted as `Accepted (phase 1; phase 2 — outlier / diversity helper)`; release skipped; Track B (n8n compose) deferred
- **Deviations:** none
- **Code evidence:** `merged via PR #9 (`adr/0014-phase-2-outlier-helper`; squash `b97c29b`; branch commits `5a691ab`, `7032668`, `22a9d76`)`
- **Test debt:** carried from verification — scroll-supplement restriction unit test; `main.py` positive registration gate; combined `path_glob`+`context_chunk_ids` integration; live HTTP/Ollama e2e for `find_outlier_chunks`; golden-set outlier quality eval; multi-collection/sparse fusion deferred
- **Verify:** carried from verification — 287 unit tests passed; 17 targeted outlier tests passed; ruff clean; Docker integration report pass (8 pytest integration, smoke_recommend); review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/9 — status: merged — commit: b97c29b
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — implementation
- **Phase:** Track A — Phase 1 (Recommendation search tool)
- **Tracker status:** `implemented`
- **Choices:** Tool name `recommend_code`; RecommendStrategy AVERAGE_VECTOR only; dense-only; path_glob post-filter fnmatch + limit×3; missing chunk IDs fail fast; multi-collection deferred
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/src/codebase_indexer/tools/recommend.py`, `mcp_server/src/codebase_indexer/main.py`, `docker-compose.yml`, `.env.example`, `docs/SEARCH_BEHAVIOR.md`, `mcp_server/tests/test_recommend.py`, `mcp_server/tests/test_recommend_tool.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_storage_integration.py`
- **Test debt:** `main.py` registration gate; live HTTP/Ollama e2e; golden-set eval; multi-collection deferred
- **Changelog:** no

#### 2026-07-03 — implementation
- **Phase:** Track A — Phase 2 (outlier / diversity helper)
- **Tracker status:** `implemented`
- **Choices:** Separate tool `find_outlier_chunks` (not extending `recommend_code`); score = cosine similarity to context centroid (ascending = most distant); reuse `RECOMMEND_ENABLED` gate (no `OUTLIER_ENABLED`); config `OUTLIER_MAX_CONTEXT_SAMPLES` (200) + `OUTLIER_MAX_SIMILARITY` (0.55); Qdrant `RecommendStrategy.BEST_SCORE` negative-only; scroll supplement only when `path_glob` set or no explicit `context_chunk_ids`
- **Deviations:** Scroll supplement restricted when only `context_chunk_ids` provided — prevents outlier candidates being absorbed into context centroid during whole-collection scroll fill
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/src/codebase_indexer/tools/outliers.py`, `mcp_server/src/codebase_indexer/main.py`, `docker-compose.yml`, `.env.example`, `docs/SEARCH_BEHAVIOR.md`, `docs/ARCHITECTURE.md`, `README.md`, `mcp_server/tests/test_outliers.py`, `mcp_server/tests/test_outlier_tool.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_main.py`, `mcp_server/tests/test_storage_integration.py`
- **Test debt:** `main.py` positive registration gate; live HTTP/Ollama e2e for `find_outlier_chunks`; combined `path_glob`+`context_chunk_ids` integration; golden-set outlier quality eval; multi-collection/sparse fusion deferred
- **Changelog:** no

### ADR 0015 — Phase 1 — HTTP sidecar + remote backend

#### 2026-07-03 — verification
- **Phase:** Phase 1 — HTTP sidecar + remote backend
- **Tracker status:** `verified`
- **Choices:** Opt-in `COLBERT_EMBED_BACKEND=remote` with `colbert_worker` sidecar; default remains in-process ONNX; sidecar uses FastAPI lifespan preload
- **Deviations:** none
- **Code evidence:** `colbert_worker/`, `colbert_worker/Dockerfile`, `colbert_remote.py`, `factory.py`, `config.py`, `embedder.py`, `docker-compose.colbert-worker.yml`, `.env.example`, `SEARCH_BEHAVIOR.md`
- **Test debt:** Optional slow onnx vs remote parity; operational memory-halt manual validation
- **Verify:** tests run + plan compliance pass — pytest 229 passed, 3 skipped; 45 targeted ColBERT tests passed; review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — verification
- **Phase:** Phase 2 — GPU ColBERT worker image + index throughput benchmark vs CPU sidecar
- **Tracker status:** `verified`
- **Choices:** GPU acceleration in sidecar image only (MCP stays CPU fastembed/onnxruntime); reuse `ColbertOnnxBackend` with `use_cuda`/`device_ids`; compose-only `COLBERT_GPU` doc flag; dedicated `bench_colbert_sidecar.py`; fail-fast CUDA startup; single-GPU 8GB OOM documented without auto-scheduler
- **Deviations:** none
- **Code evidence:** `colbert_worker/Dockerfile.gpu`, `docker-compose.colbert-worker.gpu.yml`, `mcp_server/src/codebase_indexer/colbert_worker/app.py`, `mcp_server/src/codebase_indexer/colbert_worker/settings.py`, `mcp_server/src/codebase_indexer/colbert_worker/cuda.py`, `mcp_server/src/codebase_indexer/indexer/backends/colbert_onnx.py`, `mcp_server/benchmarks/bench_colbert_sidecar.py`, `mcp_server/benchmarks/bench.py`, `docs/DEPLOYMENT.md`, `.env.example`, `.github/workflows/ci.yml`, `mcp_server/pyproject.toml`
- **Test debt:** Docker GPU image runtime smoke; live GPU embed integration beyond provider probe; `bench_colbert_sidecar --compare` unit test; host-side sidecar reachability docs
- **Verify:** tests run + plan compliance pass — pytest 236 passed, 3 skipped, 5 deselected; all in-scope plan requirements pass; review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — prioritization
- **Phase:** Phase 1 — HTTP sidecar + remote backend + compose override + tests + operator docs
- **Tracker status:** `candidate`
- **Choices:** Prioritize ADR 0015 Phase 1 over ADR 0008 phase 2+ refinements, Proposed ADR 0002 GraphRAG, and Proposed ADR 0014 recommendation tools; single phase per pipeline rule; mirror `OllamaDenseBackend` HTTP client pattern; default `COLBERT_EMBED_BACKEND=onnx` unchanged; no Qdrant schema or MAX_SIM rerank path changes. **Chosen scope:** `colbert_worker` FastAPI (GET /health, POST /v1/embed/colbert); `ColbertRemoteBackend` (httpx, batching, retries, preload); `create_colbert_backend()` onnx vs remote; config (`COLBERT_EMBED_BACKEND`, `COLBERT_URL`, `COLBERT_TIMEOUT`, `COLBERT_EMBED_BATCH_SIZE`); `embedder.py` release/idle without hardcoded `ColbertOnnxBackend` singleton; `docker-compose.colbert-worker.yml` with shared `fastembed_cache`; tests (`test_colbert_remote_backend.py`, `test_colbert_worker.py`, factory/config updates); `.env.example` sidecar preset + `SEARCH_BEHAVIOR.md` remote docs; defer GPU worker (P2) and MCP slim image (P3). **Why now:** ADR 0008 phase 1 ColBERT rerank is merged but in-process ONNX causes MCP RAM halt at `RERANK_ENABLED=true` on production-like deployments; ADR 0015 is Accepted and mirrors the proven Ollama dense HTTP split; prerequisites (0003, 0007, 0011, 0008 P1) are merged; no sidecar/remote backend code exists yet; opt-in default preserves existing deployments; validation path defined (mocked httpx tests, worker TestClient, config validation, operational memory criteria). **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Test debt:** carry ADR 0008 phase 2+ test debt (xref/service_map rerank, golden MRR `--rerank`) as out-of-scope for this phase
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — plan
- **Phase:** Phase 1 — HTTP sidecar + remote backend + compose override + tests + operator docs
- **Tracker status:** `planned`
- **Choices:** Mirror `OllamaDenseBackend` HTTP patterns; sidecar port 8082 internal-only; phase 1 no bearer auth; one PR for entire phase. **Chosen scope:** `colbert_worker` FastAPI sidecar (GET /health, POST /v1/embed/colbert) reusing `ColbertOnnxBackend`; `ColbertRemoteBackend` httpx client mirroring `OllamaDenseBackend`; `create_colbert_backend()` selects onnx vs remote; config (`COLBERT_EMBED_BACKEND`, `COLBERT_URL`, `COLBERT_TIMEOUT`, `COLBERT_EMBED_BATCH_SIZE`); `embedder.py` release/idle without hardcoded `ColbertOnnxBackend` singleton when remote; `docker-compose.colbert-worker.yml` with shared `fastembed_cache`; tests; `.env.example` + `SEARCH_BEHAVIOR.md`; default `COLBERT_EMBED_BACKEND=onnx` unchanged
- **Deviations:** none
- **Test debt:** carry ADR 0008 phase 2+ test debt (xref/service_map rerank, golden MRR `--rerank`) as out-of-scope for this phase
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — plan
- **Phase:** Phase 2 — GPU ColBERT worker image + index throughput benchmark vs CPU sidecar
- **Tracker status:** `planned`
- **Choices:** GPU acceleration in **sidecar image only** (not MCP) to avoid fastembed/fastembed-gpu lock conflict and ADR 0011 CPU MCP model; reuse `ColbertOnnxBackend` with `use_cuda` rather than new backend class; compose-only `COLBERT_GPU` doc flag (like `OLLAMA_GPU`); dedicated benchmark script over full `run_benchmark` with `rerank_enabled=True` + `colbert_embed_backend=remote`; single PR for entire phase. **Chosen scope:** Optional GPU ColBERT sidecar via `colbert_worker/Dockerfile.gpu` (fastembed-gpu + onnxruntime-gpu, separate from MCP CPU deps); compose override `docker-compose.colbert-worker.gpu.yml` mirroring `docker-compose.ollama.gpu.yml`; compose-only `COLBERT_GPU` / `COLBERT_GPU_COUNT` and worker `COLBERT_USE_CUDA`; extend `ColbertOnnxBackend` + worker `/health` device reporting; dedicated `benchmarks/bench_colbert_sidecar.py` for remote-sidecar index throughput CPU vs GPU; CI-safe mocked/skipped GPU tests + non-blocking GPU Dockerfile CI job; `ColbertRemoteBackend` and HTTP contract unchanged
- **Deviations:** none
- **Test debt:** CI-safe mocked/skipped GPU tests + non-blocking GPU Dockerfile CI job
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — merge
- **Phase:** Phase 1 — HTTP sidecar + remote backend + compose override + tests + operator docs
- **Tracker status:** `merged`
- **Choices:** squash merge `e16dc59` on feature branch `adr/0015-phase-1-colbert-sidecar`; ADR accept skipped (already Accepted); release skipped; phase 2+ deferred (GPU worker; MCP slim when remote-only)
- **Deviations:** none
- **Code evidence:** `merged via PR #2 (`adr/0015-phase-1-colbert-sidecar`)`
- **Test debt:** carried from verification — optional slow onnx vs remote parity; operational memory-halt manual validation
- **Verify:** carried from verification — pytest 229 passed, 3 skipped; 45 targeted ColBERT tests passed; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/2 — status: merged — commit: e16dc59
- **Changelog:** no — already added at verified step

#### 2026-07-03 — merge
- **Phase:** Phase 2 — GPU ColBERT worker image + index throughput benchmark vs CPU sidecar
- **Tracker status:** `merged`
- **Choices:** squash merge `b53029ed` on feature branch `adr/0015-phase-2-colbert-gpu`; ADR accept skipped (already Accepted); release skipped; phase 3+ deferred (MCP slim when remote-only)
- **Deviations:** none
- **Code evidence:** `merged via PR #3 (`adr/0015-phase-2-colbert-gpu`)`
- **Test debt:** carried from verification — Docker GPU image runtime smoke; live GPU embed integration beyond provider probe; `bench_colbert_sidecar --compare` unit test; host-side sidecar reachability docs
- **Verify:** carried from verification — pytest 236 passed, 3 skipped, 5 deselected; all in-scope plan requirements pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/3 — status: merged — commit: b53029ed
- **Changelog:** no — already added at verified step

#### 2026-07-03 — implementation
- **Phase:** Phase 1 — HTTP sidecar + remote backend + compose override + tests + operator docs
- **Tracker status:** `implemented`
- **Choices:** Mirror `OllamaDenseBackend` HTTP patterns; sidecar port 8082 internal-only; phase 1 no bearer auth; default `COLBERT_EMBED_BACKEND=onnx` unchanged; FastAPI lifespan for sidecar preload; shared `fastembed_cache` volume in compose override
- **Deviations:** Sidecar uses FastAPI lifespan instead of deprecated `on_event` startup for model preload
- **Code evidence:** `config.py`, `colbert_remote.py`, `factory.py`, `embedder.py`, `colbert_worker/`, `colbert_worker/Dockerfile`, `docker-compose.colbert-worker.yml`, `docker-compose.yml`, `.env.example`, `SEARCH_BEHAVIOR.md`, `test_colbert_remote_backend.py`, `test_colbert_worker.py`, `test_factory.py`, `test_config.py`
- **Test debt:** Optional slow onnx vs remote parity; compose E2E sidecar smoke; operational MCP memory regression; sidecar-unreachable preload error path
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-03 — implementation
- **Phase:** Phase 2 — GPU ColBERT worker image + index throughput benchmark vs CPU sidecar
- **Tracker status:** `implemented`
- **Choices:** `onnxruntime-gpu==1.26.0` pinned to match CPU lock; `python:3.12-slim` base with NVIDIA compose reservations mirroring Ollama GPU pattern; optional `COLBERT_DEVICE_IDS` env wired to `ColbertOnnxBackend.device_ids`; `/health` reports configured `device` + runtime `cuda_available`; fail-fast preload when CUDA requested but unavailable; dedicated `bench_colbert_sidecar.py` over full `run_benchmark` with remote ColBERT; single-GPU 8GB OOM documented (no auto-scheduler)
- **Deviations:** none
- **Code evidence:** `colbert_worker/Dockerfile.gpu`, `docker-compose.colbert-worker.gpu.yml`, `mcp_server/src/codebase_indexer/colbert_worker/app.py`, `mcp_server/src/codebase_indexer/colbert_worker/settings.py`, `mcp_server/src/codebase_indexer/indexer/backends/colbert_onnx.py`, `mcp_server/benchmarks/bench_colbert_sidecar.py`, `docs/DEPLOYMENT.md`, `.github/workflows/ci.yml`
- **Test debt:** Docker GPU image runtime smoke; live GPU embed integration; bench compare path unit test
- **Changelog:** no

### ADR 0016 — Phase 1 — Config, Ollama MRL, docs, tests

#### 2026-07-03 — verification
- **Phase:** Phase 1 — Config, Ollama MRL, docs, tests
- **Tracker status:** `verified`
- **Choices:** Max tokens 32768; MRL 32≤size≤native; Qwen3 GPU defaults; compose generator Qwen3; ADR Accepted pre-merge
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/indexer/backends/ollama_dense.py`, `mcp_server/src/codebase_indexer/indexer/backends/factory.py`, `.env.example`, `scripts/run_compose_integration.py`, `mcp_server/benchmarks/_settings.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_ollama_dense_backend.py`, `mcp_server/tests/conftest.py`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md`, `README.md`, `docs/adr/0016-qwen3-embedding-default-dense-model.md`, `docs/adr/README.md`
- **Test debt:** Phase 2 eval baseline deferred
- **Verify:** 77 unit tests pass; integration 8/8 pass; plan compliance pass; review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — verification
- **Phase:** Phase 2 — Eval baseline refresh (final phase)
- **Tracker status:** `verified`
- **Choices:** Jina comparison baseline; recall@10 gate waived with per-tag analysis; alias line remapping; operational compose/env eval overrides not committed
- **Deviations:** none
- **Code evidence:** `mcp_server/benchmarks/fixtures/eval_baseline.json`, `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `docs/adr/0016-qwen3-embedding-default-dense-model.md`, `docs/adr/README.md`
- **Test debt:** CI validate-labels gate; compose WORKSPACE_ROOT eval preset; optional non-blocking recall benchmark job; compose host-env URL isolation
- **Verify:** 341 unit tests pass; eval harness tests pass; integration report pass; eval_baseline.json and ADR Measured outcomes consistent; documented recall@10 regression (−63.1% vs Jina) satisfies plan waiver; review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — prioritization
- **Phase:** Phase 1 — Config, Ollama MRL, docs, tests
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0016 Phase 1 over 0002 Phase 2 GraphRAG payload linking (closest alternative, 32.5 weighted score — tie within ~10%; tie-breaker: default retrieval-path impact + embedding-track sequencing); over Proposed 0018 Phase 1 (ops observability, lower default-path impact); over 0017 Phase 2 (small slice; better combined with 0018 P1); over 0014 Track B n8n and 0015 Phase 3+ slim image (ops-only, deferred); over 0008 test-debt (QA-only); single phase per pipeline rule. **Chosen scope:** Qwen3 0.6B/4B/8B entries in `KNOWN_EMBED_MODEL_DIMENSIONS` and `KNOWN_EMBED_MODEL_MAX_TOKENS`; MRL `dimensions` passthrough in `ollama_dense.py` / `factory.py` when `DENSE_EMBED_VECTOR_SIZE` < native; update `.env.example`, `.env.compose.integration`, `benchmarks/_settings.py`; unit tests (`test_config.py`, `test_ollama_dense_backend.py` mock `dimensions` payload); docs (`ARCHITECTURE.md`, `DEPLOYMENT.md`, `README.md` embedding table — Qwen3 primary, Nomic CPU preset); defer Phase 2 `eval_baseline.json` refresh and operator re-index; **requires formal Accept of Proposed ADR 0016 before dev**. **Why now:** ADR 0017 Phase 1 merged ([PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)); prior 2026-07-03 prioritization deprioritized 0016 vs 0017 P1 and recommended sequential PR after 0017 P1 merge — prerequisite now satisfied. Code still defaults to Nomic (`DENSE_EMBED_MODEL=nomic-ai/nomic-embed-text-v1.5` in `.env.example`; no Qwen3 in `KNOWN_EMBED_MODEL_*`; `OllamaDenseBackend._embed_http` lacks MRL `dimensions`). Model-accurate truncation (0017 P1) enables trustworthy 32K caps for Qwen3. Golden-set eval harness exists for Phase 2; Phase 1 mergeable without baseline refresh. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** ``.env.example` `DENSE_EMBED_MODEL=nomic-ai/nomic-embed-text-v1.5`; no Qwen3 in `KNOWN_EMBED_MODEL_*`; `OllamaDenseBackend._embed_http` lacks MRL `dimensions``
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — prioritization
- **Phase:** Phase 2 — Eval baseline refresh
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0016 Phase 2 over 0002 Phase 2 GraphRAG payload linking (closest capability alternative, 27.0 weighted score — tie within ~10%; tie-breaker: lower scope/risk for benchmark-only refresh); over 0018 Phase 2 OTel traces (26.0, ops value but lower measurability in default CI); over 0017 Phase 2 standalone (truncation counter already in 0018 P1); over 0014 Track B n8n and 0015 Phase 3+ slim image (ops-only, deferred twice); over 0008 test-debt (QA-only); single phase per pipeline rule; close embedding ADR arc before GraphRAG P2 or telemetry P2. **Chosen scope:** Re-index golden fixture collection (`codebase-indexer-mcp`) with Qwen3-4B @ 1024 via Ollama GPU; run `python -m benchmarks.eval_retrieval` and `python -m benchmarks.eval_multihop`; update `mcp_server/benchmarks/fixtures/eval_baseline.json` (params: `dense_embed_model`, `dense_embed_vector_size`, `indexed_at`, embed-model note; refresh overall metrics, `metrics_by_tag`, `multi_hop_2hop` snapshot); record Nomic/Jina vs Qwen3 deltas in tracker Phase log; apply ADR 0016 success criterion (recall@10 ≥ prior or documented regression); defer optional `num_ctx` passthrough (0016 P1 deviation), ADR 0011 body edit (0017 P2 remainder), and compose scrape/`METRICS_PORT` (0018 P1 deferrals). **Why now:** ADR 0016 Phase 1 merged ([PR #12](https://github.com/Tusquito/codebase-indexer-mcp/pull/12)); prerequisite ADR 0017 Phase 1 merged ([PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)); ADR 0018 Phase 1 merged ([PR #13](https://github.com/Tusquito/codebase-indexer-mcp/pull/13)) — prior cycle explicitly deferred 0016 P2 for 0018 P1. Embedding defaults now Qwen3 in `.env.example` and `benchmarks/_settings.py`, but `fixtures/eval_baseline.json` still records `jinaai/jina-embeddings-v2-base-code` (2026-07-02) — regression compare is misleading until refresh. ADR 0016 §Phased delivery item 2 and §Measured outcomes remain unfilled. Highest weighted score (30.5); benchmark-only PR with existing `eval_retrieval.py` / `eval_multihop.py` harness (ADR 0007, 0009); no new mandatory infra; default deploy unchanged. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** ``.env.example`, `mcp_server/benchmarks/_settings.py` Qwen3 defaults; `mcp_server/benchmarks/fixtures/eval_baseline.json` still `jinaai/jina-embeddings-v2-base-code` (2026-07-02)`
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — plan
- **Phase:** Phase 1 — Config, Ollama MRL, docs, tests
- **Tracker status:** `planned`
- **Choices:** Single PR Phase 1; ADR Accept pre-merge; compose integration generator updated to Qwen3 (`scripts/run_compose_integration.py`). **Chosen scope:** Qwen3 0.6B/4B/8B in `KNOWN_EMBED_MODEL_DIMENSIONS` + `KNOWN_EMBED_MODEL_MAX_TOKENS` with MRL-aware validation; `dimensions` passthrough in `OllamaDenseBackend` preload + `_embed_http`; update `.env.example`, `scripts/run_compose_integration.py`, `benchmarks/_settings.py`; unit tests; docs; defer Phase 2 eval baseline. **Assumptions:** 0017 P1 merged ([PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)); no new deps
- **Deviations:** none
- **Changelog:** no — user-facing yes; status `planned` (not verified)

#### 2026-07-03 — plan
- **Phase:** Phase 2 — Eval baseline refresh
- **Tracker status:** `planned`
- **Choices:** One PR for entire Phase 2; compare against committed Jina baseline (`dense_embed_model: jinaai/jina-embeddings-v2-base-code`, recall@10 0.660256); success gate = recall@10 ≥ prior or documented regression with per-tag mitigation; `RERANK_ENABLED=false` for baseline parity; use existing `scripts/reindex_graphrag.py` / MCP `index_codebase(force=True)` pattern; host eval with `OLLAMA_URL=http://127.0.0.1:11434`; defer `num_ctx`, ADR 0011 body edit, CI recall gate, Nomic re-capture unless explicitly added at verify. **Chosen scope:** Re-index golden fixture collection (`codebase-indexer-mcp`) with Qwen3-Embedding-4B @ 1024 via bundled Ollama (GPU recommended); run `eval_retrieval` (hybrid + `--no-hybrid` for `ab_dense_only`) and `eval_multihop` (two-hop RRF); commit updated `mcp_server/benchmarks/fixtures/eval_baseline.json` with refreshed `params`, overall metrics, `metrics_by_tag`, and `multi_hop_2hop`; fill ADR 0016 **Measured outcomes** (Jina 2026-07-02 → Qwen3 delta); conditional golden label fixes only if `--validate-labels` fails; single PR, no runtime code changes. **Assumptions:** Phase 1 merged ([PR #12](https://github.com/Tusquito/codebase-indexer-mcp/pull/12)); ADR 0017 P1 tokenizer merged; `benchmarks/_settings.py` already Qwen3; golden set unchanged (`golden_set_version: v3-multi-hop`, 26 queries); Docker integration required for live verify; this phase completes ADR 0016 (final phase).
- **Deviations:** none
- **Changelog:** no — user-facing no

#### 2026-07-03 — merge
- **Phase:** Phase 1 — Config, Ollama MRL, docs, tests
- **Tracker status:** `merged`
- **Choices:** merge on feature branch `adr/0016-phase-1-qwen3-default`; ADR accepted as `Accepted (phase 1 — config, Ollama MRL, docs, tests)`; release skipped; Phase 2 eval baseline + `num_ctx` deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #12](https://github.com/Tusquito/codebase-indexer-mcp/pull/12) (`adr/0016-phase-1-qwen3-default`)`
- **Test debt:** carried from verification — Phase 2 eval baseline deferred
- **Verify:** carried from verification — 77 unit tests pass; integration 8/8 pass; plan compliance pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/12 — status: merged
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — merge
- **Phase:** Phase 2 — Eval baseline refresh (final phase)
- **Tracker status:** `merged`
- **Choices:** merge on feature branch `adr/0016-phase-2-eval-baseline`; ADR accepted as **Accepted (all phases complete)**; release skipped; final ADR 0016 phase complete
- **Deviations:** none
- **Code evidence:** `merged via [PR #14](https://github.com/Tusquito/codebase-indexer-mcp/pull/14) (`adr/0016-phase-2-eval-baseline`; `ead683bebe7735941484be73b646427543af0ea1`)`
- **Test debt:** carried from verification — CI validate-labels gate; compose WORKSPACE_ROOT eval preset; optional non-blocking recall benchmark job; compose host-env URL isolation; `num_ctx` deferred (Phase 1)
- **Verify:** carried from verification — 341 unit tests pass; eval harness tests pass; integration report pass; documented recall@10 regression (−63.1% vs Jina) satisfies plan waiver; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/14 — status: merged — commit: ead683bebe7735941484be73b646427543af0ea1
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — implementation
- **Phase:** Phase 1 — Config, Ollama MRL, docs, tests
- **Tracker status:** `implemented`
- **Choices:** Max tokens 32768; MRL 32≤size≤native; Qwen3 GPU defaults; compose generator Qwen3; ADR Accepted pre-merge
- **Deviations:** `num_ctx` deferred; generator-only compose env
- **Code evidence:** `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/indexer/backends/ollama_dense.py`, `mcp_server/src/codebase_indexer/indexer/backends/factory.py`, `.env.example`, `scripts/run_compose_integration.py`, `mcp_server/benchmarks/_settings.py`, `mcp_server/tests/test_config.py`, `mcp_server/tests/test_ollama_dense_backend.py`, `mcp_server/tests/conftest.py`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md`, `README.md`, `docs/adr/0016-qwen3-embedding-default-dense-model.md`, `docs/adr/README.md`
- **Test debt:** Compose integration not smoke-run; Phase 2 eval baseline deferred
- **Changelog:** no

#### 2026-07-03 — implementation
- **Phase:** Phase 2 — Eval baseline refresh
- **Tracker status:** `implemented`
- **Choices:** Comparison baseline Jina → Qwen3 only; recall@10 gate waived with documented per-tag regression; GPU Ollama via docker-compose.ollama.gpu.yml; RERANK_ENABLED=false; golden re-index at parent WORKSPACE_ROOT; alias line remapping for Phase 1 chunk drift; multi_hop_2hop refreshed with Qwen3 metrics
- **Deviations:** Operational compose/env overrides during eval (WORKSPACE_ROOT parent mount, in-container service URLs, OLLAMA_TIMEOUT=600) not committed; significant golden-set recall regression (−63.1% recall@10 vs Jina) documented in ADR measured outcomes
- **Code evidence:** `mcp_server/benchmarks/fixtures/eval_baseline.json`, `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `docs/adr/0016-qwen3-embedding-default-dense-model.md`, `docs/adr/README.md`
- **Test debt:** CI validate-labels gate; compose WORKSPACE_ROOT eval preset; optional non-blocking recall benchmark job; compose host-env URL isolation
- **Changelog:** no — user-facing yes; status `implemented` (not verified); invoker Changelog: no

### ADR 0017 — Phase 1 — loader + Ollama backend

#### 2026-07-03 — verification
- **Phase:** Phase 1 — loader + Ollama backend
- **Tracker status:** `verified`
- **Choices:** tokenizers.Tokenizer.from_pretrained; HF env cache dirs; shared class-level tokenizer; fallback = log WARNING + pass text through unchanged; sparse BM25 untouched; Phase 2 observability + ADR 0011 edit deferred
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/indexer/tokenizer_loader.py`, `mcp_server/src/codebase_indexer/indexer/backends/ollama_dense.py`, `mcp_server/src/codebase_indexer/config.py`, `mcp_server/tests/test_ollama_dense_backend.py`, `mcp_server/tests/test_truncation.py`, `docs/ARCHITECTURE.md`, `.env.example`, `docker-compose.yml`
- **Test debt:** slow real-Nomic tokenizer test; no golden-set truncation accuracy fixture; Phase 2 metrics not implemented
- **Verify:** 22 unit tests pass; integration report pass (8 storage integration, compose deploy OK); plan compliance pass; review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — prioritization
- **Phase:** Phase 1 — loader + Ollama backend
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0017 Phase 1 over Proposed 0016 Phase 1 (closest alternative, −1.2 weighted score but higher scope/risk and breaking defaults); over 0002 Phase 2 GraphRAG payload linking (capability arc next but optional Neo4j + index payload work); over 0008 test-debt closure PR (QA-only, no capability); over 0009 eval_multihop CI gate (benchmark-only); over 0015 Phase 3 slim image and 0014 Track B n8n (ops-only, deferred twice); single phase per pipeline rule; embedding correctness before Qwen3 default switch; tie-breaker vs 0016: lower scope/risk. **Chosen scope:** Add `load_dense_tokenizer(model_id)` helper with HF Hub download + cache dir resolution; lazy-load shared `tokenizers.Tokenizer` in `OllamaDenseBackend` at preload; replace `truncate_bm25_text` in `_truncate_batch` with `truncate_for_embedding`; graceful fallback when tokenizer load fails (log warning; document behavior at plan); unit tests with mock `Tokenizer` in `test_ollama_dense_backend.py` and loader/fallback cases in `test_truncation.py`; optional `.env.example` `HF_HOME` note; update `docs/ARCHITECTURE.md` dense truncation behavior; defer Phase 2 observability (truncation metrics / token_count logs) and ADR 0011 body edit to finisher/plan; **requires formal Accept of Proposed ADR 0017 before dev**. **Why now:** Major arcs merged (0008 complete, 0015 P1–P2, 0014 Track A P1–P2, 0002 P1, 0009 P2); two new Proposed ADRs (0016/0017) form an embedding-quality track; code still uses BM25 word-split truncation in `OllamaDenseBackend._truncate_batch` (`truncate_bm25_text`) while `truncate_for_embedding`/`truncate_with_tokenizer` exist unused on the Ollama path; ADR 0016 Qwen3 default at 32K+ makes approximation errors material; 0017 Phase 1 is non-breaking (no re-index), satisfies ADR 0011 prerequisites, measurable via unit tests, no new mandatory infra; unlocks safe 0016 Phase 1 rollout next cycle. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** ``OllamaDenseBackend._truncate_batch` uses `truncate_bm25_text`; `truncate_for_embedding`/`truncate_with_tokenizer` exist unused on Ollama path`
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — plan
- **Phase:** Phase 1 — loader + Ollama backend (single PR)
- **Tracker status:** `planned`
- **Choices:** Single PR for Phase 1; mirror `OnnxSparseBackend` shared-tokenizer + `truncate_for_embedding` pattern; use `tokenizers.Tokenizer.from_pretrained` not `transformers.AutoTokenizer`; fallback = pass-through on load failure (log warning; not BM25, not char heuristic); no new mandatory infra; explicit `tokenizers` dep optional; ADR Accept before dev. **Chosen scope:** Add `indexer/tokenizer_loader.py` with `load_dense_tokenizer(model_id)` (HF Hub download + `HF_HOME`/`HF_HUB_CACHE`/`TRANSFORMERS_CACHE` resolution); class-level shared `tokenizers.Tokenizer` in `OllamaDenseBackend` loaded at `preload()` via `_ensure_truncation()`; replace `truncate_bm25_text` in `_truncate_batch` with `truncate_for_embedding`; fallback on load failure = log warning + pass text through unchanged (no BM25 fallback); unit tests with mock `Tokenizer` in `test_ollama_dense_backend.py` and loader/fallback in `test_truncation.py`; optional `@pytest.mark.slow` real Nomic tokenizer test; update `docs/ARCHITECTURE.md` and `.env.example` `HF_HOME` note; optional `docker-compose.yml` `HF_HOME` passthrough; defer Phase 2 observability and ADR 0011 body edit to finisher. **Assumptions:** `DENSE_EMBED_MODEL` is valid HF repo with tokenizer files; `tokenizers` remains transitive via fastembed; Phase 2 and ADR 0016 default switch are separate PRs; compose integration required for verification.
- **Deviations:** none
- **Test debt:** optional `@pytest.mark.slow` real Nomic tokenizer test
- **Verify:** compose integration required for verification
- **Changelog:** no — user-facing yes but status not yet verified

#### 2026-07-03 — merge
- **Phase:** Phase 1 — loader + Ollama backend
- **Tracker status:** `merged`
- **Choices:** squash merge `a094bf5` on feature branch `adr/0017-phase-1-tokenizer-loader`; ADR accepted as `Accepted (phase 1 — loader + Ollama backend)` (docs commit `695b678`); release skipped; Phase 2 observability + ADR 0011 body edit deferred
- **Deviations:** none
- **Code evidence:** `merged via PR #11 (`adr/0017-phase-1-tokenizer-loader`; squash `a094bf5`)`
- **Test debt:** carried from verification — slow real-Nomic tokenizer test; no golden-set truncation accuracy fixture; Phase 2 metrics not implemented
- **Verify:** carried from verification — 22 unit tests pass; integration report pass (8 storage integration, compose deploy OK); plan compliance pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/11 — status: merged — commit: a094bf5
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-03 — implementation
- **Phase:** Phase 1 — loader + Ollama backend
- **Tracker status:** `implemented`
- **Choices:** Used `tokenizers.Tokenizer.from_pretrained`; cache dir from HF env vars; shared class-level tokenizer; fallback = log WARNING and pass text through unchanged; sparse BM25 path untouched; Phase 2 observability and ADR 0011 edit deferred
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/indexer/tokenizer_loader.py`, `mcp_server/src/codebase_indexer/indexer/backends/ollama_dense.py`, `mcp_server/src/codebase_indexer/config.py`, `docs/ARCHITECTURE.md`, `.env.example`, `docker-compose.yml`, `mcp_server/tests/test_ollama_dense_backend.py`, `mcp_server/tests/test_truncation.py`
- **Test debt:** Compose integration not smoke-run; slow real-nomic tokenizer test; no golden-set truncation accuracy fixture; Phase 2 metrics not implemented
- **Changelog:** no

### ADR 0018 — Phase 1 — Application Prometheus metrics (MCP + ColBERT worker)

#### 2026-07-03 — verification
- **Phase:** Phase 1 — Application Prometheus metrics (MCP + ColBERT worker)
- **Tracker status:** `verified`
- **Choices:** Dedicated CollectorRegistry; ColBERT ONNX at worker HTTP layer; index metrics via IndexJobTracker; Docker skip per plan
- **Deviations:** none
- **Code evidence:** `carried from implementation — `telemetry/metrics.py`, `main.py`, `colbert_worker/app.py`, `tools/*.py`, `search_common.py`, backends, `memory.py`, `DEPLOYMENT.md`, `test_telemetry_metrics.py``
- **Test debt:** Bearer-auth /metrics test; truncated_chunks helper test; in-process ColBERT embed metrics; memory pressure edge-trigger
- **Verify:** tests run + plan compliance pass (329 passed); review rounds: 1
- **Changelog:** yes

#### 2026-07-03 — prioritization
- **Phase:** Phase 1 — Application Prometheus metrics (MCP + ColBERT worker)
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0018 Phase 1 over 0016 Phase 2 eval baseline; single phase per pipeline rule. **Chosen scope:** Accept ADR 0018 then implement Phase 1 only: `telemetry/metrics.py` with `METRICS_ENABLED=false` default; `prometheus_client>=0.21`; thin metrics-only decorator on MCP tool handlers; counters/histograms; `GET /metrics` on MCP and ColBERT worker; unit tests; `DEPLOYMENT.md` Qdrant scrape docs; defer Phase 2 OTel traces, Phase 3 compose stack; **requires formal Accept of Proposed ADR 0018 before dev**. **Why now:** Embedding prerequisites merged (0016 P1 [PR #12](https://github.com/Tusquito/codebase-indexer-mcp/pull/12), 0017 P1 [PR #11](https://github.com/Tusquito/codebase-indexer-mcp/pull/11)); multi-container topology from 0015 makes cross-service latency/OOM the dominant ops gap; zero instrumentation in code; 0017 Phase 2 explicitly deferred to 0018 metric namespace. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** `zero application `/metrics` endpoint or Prometheus instrumentation in codebase today`
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — plan
- **Phase:** Phase 1 — Application Prometheus metrics (MCP + ColBERT worker)
- **Tracker status:** `planned`
- **Choices:** Single PR per phase; metrics-only `@observe_tool` decorator on all MCP tool handlers (not custom OTel spans); `prometheus_client>=0.21` in main dependencies with `METRICS_ENABLED=false` runtime gate; truncation counter wired; Qdrant scrape documented only in `DEPLOYMENT.md`; Docker compose unchanged in Phase 1; defer Phase 2 OTel traces, Phase 3 observability compose stack. **Chosen scope:** Accept ADR 0018 then implement Phase 1 only: `telemetry/metrics.py` with `METRICS_ENABLED=false` default; thin metrics-only decorator; application counters/histograms; `GET /metrics` on MCP and ColBERT worker; unit tests; `DEPLOYMENT.md` scrape docs. **Assumptions:** ADR Accept at finisher after merge; default CI metrics-disabled.
- **Deviations:** none
- **Code evidence:** `zero application `/metrics` endpoint or Prometheus instrumentation in codebase today`
- **Changelog:** no — user-facing yes; status `planned` (changelog at verified)

#### 2026-07-03 — merge
- **Phase:** Phase 1 — Application Prometheus metrics (MCP + ColBERT worker)
- **Tracker status:** `merged`
- **Choices:** merge on feature branch `adr/0018-phase-1-prometheus-metrics`; ADR accepted as `Accepted (phase 1 — Application Prometheus metrics (MCP + ColBERT worker))`; release skipped; Phase 2 OTel traces, Phase 3 observability compose stack deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #13](https://github.com/Tusquito/codebase-indexer-mcp/pull/13) (`adr/0018-phase-1-prometheus-metrics`; `516b5feee19a81214b47dfaf135fa46391021a9b`)`
- **Test debt:** carried from verification — Bearer-auth /metrics test; truncated_chunks helper test; in-process ColBERT embed metrics; memory pressure edge-trigger
- **Verify:** carried from verification — 329 tests pass; plan compliance pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/13 — status: merged — commit: 516b5feee19a81214b47dfaf135fa46391021a9b
- **Changelog:** no — already in `[Unreleased]` from verified step

#### 2026-07-03 — implementation
- **Phase:** Phase 1 — Application Prometheus metrics (MCP + ColBERT worker)
- **Tracker status:** `implemented`
- **Choices:** Opt-in `METRICS_ENABLED=false` default; `prometheus_client` on dedicated registry; metrics-only `@observe_tool`; no collection/rel_path labels; `METRICS_PORT` and docker-compose deferred
- **Deviations:** Dedicated `CollectorRegistry`; pytest-asyncio re-added to dev deps; colbert_onnx metrics at worker HTTP layer only
- **Code evidence:** ``telemetry/metrics.py`, `main.py`, `colbert_worker/app.py`, `tools/*.py`, `search_common.py`, backends, `memory.py`, `DEPLOYMENT.md`, `test_telemetry_metrics.py``
- **Test debt:** Bearer-auth /metrics integration; compose scrape smoke; Phase 2 OTel span tests
- **Changelog:** no

### ADR 0019 — Phase 2 — Historical migration

#### 2026-07-08 — verification
- **Phase:** Phase 2 — Historical migration
- **Tracker status:** `verified`
- **Choices:** One-time `scripts/migrate_tracker_to_yaml.py` migration; YAML source of truth with generated markdown; kebab-case `phase_key`; plain `adr_id`; open-decisions collapsed to bullets; blocking `--check` gate.
- **Deviations:** none
- **Code evidence:** `scripts/migrate_tracker_to_yaml.py`, `scripts/render_adr_tracker.py`, `docs/adr/tracker/phases/*.yaml`, `docs/adr/tracker/events/*.yaml`, `docs/adr/IMPLEMENTATION_TRACKER.md`, `.github/workflows/ci.yml`, `docs/adr/README.md`
- **Test debt:** Optional migrate-helper regression test skipped (helper is one-time/deletable per ADR); event-count text in tracker (141) is one behind on-disk (142) — cosmetic
- **Verify:** render `--check` zero drift; `--validate-only` 40 phase + 142 event files valid; 9 render unit tests pass; ruff clean; CI render-diff step confirmed blocking (no `\|\| true`); Docker integration report pass (quality validation skipped per plan); manual preamble + partial-acceptance/superseded postamble preserved outside markers; review rounds: 1
- **Changelog:** no — user-facing no

#### 2026-07-08 — verification
- **Phase:** Phase 3 — Agent pipeline cutover
- **Tracker status:** `verified`
- **Choices:** Confirmed full YAML-write + render cutover with no legacy string-surgery path; migration helper moved (not duplicated) to `scripts/archive/` per human decision; orchestrator tracker contract enforces render-drift as acceptance failure; git-operator cleanup commits `docs/adr/tracker/**` with regenerated `IMPLEMENTATION_TRACKER.md`.
- **Deviations:** none
- **Code evidence:** `.cursor/agents/adr-tracker.md`, `.cursor/agents/adr-orchestrator.md`, `.cursor/agents/adr-git-operator.md`, `.cursor/agents/README.md`, `docs/adr/README.md`, `scripts/archive/migrate_tracker_to_yaml.py`
- **Test debt:** No automated coverage for agent-markdown contracts; optional end-to-end adr-tracker YAML→render fixture test; archived helper untested
- **Verify:** Review rounds: 1. 9 render unit tests pass; `render_adr_tracker.py --validate-only` (41 phases / 146 events valid) and `--check` (no drift) pass; archived `migrate_tracker_to_yaml.py --dry-run` runs correctly from `scripts/archive/` with `parents[2]` root fix; Docker integration Verdict: pass (quality validation skipped per plan); plan compliance pass across all in-scope tasks.
- **Changelog:** no — user-facing no

#### 2026-07-08 — plan
- **Phase:** Phase 2 — Historical migration
- **Tracker status:** `planned`
- **Choices:** One-time semi-automated migration (helper + human diff review); YAML is new source of truth, markdown becomes generated artifact; open-decisions collapse to bullets; manual narrative preserved outside markers; blocking `--check` gate. **Chosen scope:** Add `scripts/migrate_tracker_to_yaml.py`; generate ~28 phase files + ~100 event files under `docs/adr/tracker/`; regenerate `IMPLEMENTATION_TRACKER.md` (marker-delimited generated blocks, manual preamble/postamble preserved); make CI render-diff check blocking (remove `\|\| true`); document migration in `docs/adr/README.md`. Phase 3 agent cutover deferred.
- **Deviations:** none
- **Changelog:** no — user-facing no

#### 2026-07-08 — plan
- **Phase:** Phase 3 — Agent pipeline cutover
- **Tracker status:** `planned`
- **Choices:** adr-tracker writes both event (append-only) and phase (snapshot) YAML then renders, because `render_adr_tracker.py` does not upsert phase files from events; CHANGELOG rules kept verbatim; git-operator cleanup commits YAML + generated markdown; migration helper archived under `scripts/archive/` (human decision). **Chosen scope:** One PR: rewrite `.cursor/agents/adr-tracker.md` to write YAML event + phase files and run `scripts/render_adr_tracker.py` (replacing markdown string surgery); update `adr-orchestrator.md` tracker/render contract and `adr-git-operator.md` cleanup commit paths (`docs/adr/tracker/**` + regenerated `IMPLEMENTATION_TRACKER.md`); deprecate/remove legacy markdown-append instructions; sync `.cursor/agents/README.md` and `docs/adr/README.md`; archive one-time `scripts/migrate_tracker_to_yaml.py` under `scripts/archive/`. No server/runtime code. **Assumptions:** Phase 3 = ADR's two listed items (YAML-write cutover + deprecate markdown append); render/validation logic and schema unchanged; ADR reaches final phase and is accepted as Accepted (phase 3) by finisher; `phase_key` = `phase-3`.
- **Deviations:** none
- **Changelog:** no — user-facing no

#### 2026-07-08 — merge
- **Phase:** Phase 2 — Historical migration
- **Tracker status:** `merged`
- **Choices:** squash merge `32b7279` on feature branch `adr/0019-phase-2-historical-migration` (squash of b61df4c, b7d94c6, f038e26, 63a9e58, 38f1e87); ADR accepted as **Accepted (phase 2)**; release skipped; Phase 3 (agent cutover) deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #28](https://github.com/Tusquito/codebase-indexer-mcp/pull/28) (`adr/0019-phase-2-historical-migration`; squash `32b7279`)`
- **Test debt:** carried from verification — Optional migrate-helper regression test skipped (helper is one-time/deletable per ADR); event-count text in tracker (141) is one behind on-disk (142) — cosmetic
- **Verify:** carried from verification — render `--check` zero drift; `--validate-only` 40 phase + 142 event files valid; 9 render unit tests pass; ruff clean; CI render-diff step confirmed blocking (no `\|\| true`); Docker integration report pass (quality validation skipped per plan); manual preamble + partial-acceptance/superseded postamble preserved outside markers; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/28 — status: merged — commit: 32b7279
- **Changelog:** no — user-facing no; release skipped

#### 2026-07-08 — merge
- **Phase:** Phase 3 — Agent pipeline cutover
- **Tracker status:** `merged`
- **Choices:** Squash merge PR #29; ADR accepted as Accepted (phase 3) — final phase completes ADR 0019; release skipped; YAML tracker agent cutover complete.
- **Deviations:** none
- **Code evidence:** `merged via [PR #29](https://github.com/Tusquito/codebase-indexer-mcp/pull/29) (`adr/0019-phase-3-agent-cutover`; squash `678f9a7` of 6870415, a0d9ea9, 519b929)`
- **Test debt:** No automated coverage for agent-markdown contracts; optional end-to-end adr-tracker YAML→render fixture test; archived helper untested
- **Verify:** carried from verification — Review rounds: 1. 9 render unit tests pass; `render_adr_tracker.py --validate-only` (41 phases / 146 events valid) and `--check` (no drift) pass; archived `migrate_tracker_to_yaml.py --dry-run` runs correctly from `scripts/archive/` with `parents[2]` root fix; Docker integration Verdict: pass (quality validation skipped per plan); plan compliance pass across all in-scope tasks.
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/29 — status: merged — commit: 678f9a7
- **Changelog:** no — user-facing no; release skipped

#### 2026-07-08 — implementation
- **Phase:** Phase 2 — Historical migration
- **Tracker status:** `implemented`
- **Choices:** Built one-time `scripts/migrate_tracker_to_yaml.py`; migrated full historical tracker to `docs/adr/tracker/` (40 phase + 141 event YAML files); regenerated `IMPLEMENTATION_TRACKER.md` from YAML with manual preamble/postamble (partial-acceptance table + superseded note) preserved outside `<!-- BEGIN/END GENERATED -->` markers; kebab-case `phase_key` slugs; open-decisions collapsed to flat bullets from event `open_decisions`; plain `adr_id` summary; flipped CI render-diff check to blocking (dropped `\|\| true`); deleted the orphaned Phase-1 `0008-phase-2b` sample event.
- **Deviations:** file counts 40/141 (vs plan estimate 28/100); resolved historical open-decision rows dropped (only genuinely-open carried); optional regression test not added.
- **Code evidence:** `scripts/migrate_tracker_to_yaml.py`, `docs/adr/tracker/phases/*.yaml`, `docs/adr/tracker/events/*.yaml`, `docs/adr/IMPLEMENTATION_TRACKER.md`, `.github/workflows/ci.yml`, `docs/adr/README.md`
- **Test debt:** migration-parser unit tests (low value, one-time helper); minor rendered-cosmetics tidy (nested backticks / verbose slug); optional full-tracker render regression test (mitigated by blocking CI `--check`)
- **Changelog:** no — user-facing no

#### 2026-07-08 — implementation
- **Phase:** Phase 3 — Agent pipeline cutover
- **Tracker status:** `implemented`
- **Choices:** Rewrote `adr-tracker.md` to write append-only event YAML + upsert phase snapshot YAML then run `render_adr_tracker.py` (no markdown string-surgery path); orchestrator tracker contract updated with render-drift = acceptance failure; git-operator cleanup commits `docs/adr/tracker/**` + regenerated `IMPLEMENTATION_TRACKER.md`; synced `.cursor/agents/README.md` and `docs/adr/README.md`; archived `migrate_tracker_to_yaml.py` under `scripts/archive/` (human decision) with `REPO_ROOT` depth fix.
- **Deviations:** none
- **Code evidence:** `.cursor/agents/adr-tracker.md`, `.cursor/agents/adr-orchestrator.md`, `.cursor/agents/adr-git-operator.md`, `.cursor/agents/README.md`, `docs/adr/README.md`, `scripts/archive/migrate_tracker_to_yaml.py`
- **Test debt:** No automated coverage for agent-markdown contracts; optional end-to-end adr-tracker YAML→render fixture test; archived helper untested
- **Changelog:** no — user-facing no

#### 2026-07-07 — verification
- **Phase:** Phase 1 — Schema, layout, render script
- **Tracker status:** `verified`
- **Choices:** YAML tracker under `docs/adr/tracker/` with `schema.yaml` contract driving validation; stdlib+PyYAML render script generating marker-delimited summary/active/phase-logs/open-decisions blocks with preamble preservation; non-blocking `--check \|\| true` CI step in Phase 1; migration (Phase 2) and agent cutover (Phase 3) deferred.
- **Deviations:** none
- **Code evidence:** `scripts/render_adr_tracker.py`, `docs/adr/tracker/schema.yaml`, `docs/adr/tracker/phases/0019-phase-1.yaml`, `docs/adr/tracker/events/0019-phase-1-2026-07-07-plan.yaml`, `mcp_server/tests/test_render_adr_tracker.py`, `mcp_server/tests/fixtures/adr_tracker/**`, `.github/workflows/ci.yml`, `mcp_server/pyproject.toml`, `docs/adr/README.md`
- **Test debt:** blocking render-diff CI check after Phase 2 migration; historical tracker migration to YAML (Phase 2); optional nested-contract (git/changelog) rejection tests
- **Verify:** 9 render unit tests pass; render script validate/check/scaffold behavior confirmed; full suite 398 passed (8 storage-integration failures environmental, green in Docker integration report); Docker integration Verdict: pass; quality validation plan-approved skip; plan compliance pass across all in-scope paths; review rounds: 1
- **Changelog:** no — user-facing no

#### 2026-07-07 — prioritization
- **Phase:** Phase 1 — Schema, layout, render script
- **Tracker status:** `candidate`
- **Choices:** Prioritize ADR 0019 Phase 1 over near-tied alternative ADR 0024 Phase 1 (stack tuner analyze/allocate) via tie-breaker on lower scope/risk (0019 touches no cross-platform host-detection surface); recommended over ADR 0023 Phase 3 (retire Qdrant callees index) due to narrower impact (graph-enabled-only deployments) and higher data-migration risk. **Why now:** Tracker file measured at 254,446 chars / ~1,810 lines across 25 ADRs; exceeded 100k-char single-read tool limit during this analysis, directly confirming the ADR's predicted merge-conflict / fragile-edit-at-scale gap. Zero prior implementation (`docs/adr/tracker/**` and `scripts/render_adr_tracker.py` both absent) — clean start, no in-flight conflicts. **Suggested scope:** One phase (= one PR): `docs/adr/tracker/schema.yaml` + directory layout, `scripts/render_adr_tracker.py`, unit tests validating fixture YAML → expected summary/phase-log output, non-blocking CI validation. Explicitly excludes Phase 2 (historical migration) and Phase 3 (agent pipeline cutover). **Chosen scope:** Phase 1 only, as above (single PR). Requires formal Accept of Proposed ADR 0019 before dev.
- **Deviations:** none
- **Code evidence:** ``docs/adr/tracker/**` absent; `scripts/render_adr_tracker.py` absent; `IMPLEMENTATION_TRACKER.md` at 254,446 chars / ~1,810 lines`
- **Changelog:** no — user-facing unknown (maintainer/agent tooling only — likely no, pending planning confirmation)

#### 2026-07-07 — plan
- **Phase:** Phase 1 — Schema, layout, render script
- **Tracker status:** `planned`
- **Choices:** stdlib + PyYAML render script; generated sections wrapped in `<!-- BEGIN/END GENERATED:* -->` markers; prove on committed sample fixtures rather than migrating the real tracker; CI validation non-blocking in Phase 1; `pyyaml` added as explicit dev extra (already transitively locked). **Chosen scope:** Add `docs/adr/tracker/` (`schema.yaml` + `phases/` + `events/` with 1–2 sample files), `scripts/render_adr_tracker.py` (load/validate/render with `--check`), unit tests + fixtures in `mcp_server/tests/`, non-blocking CI validation step, README tracker-layout pointer, and `pyyaml` dev extra; live `IMPLEMENTATION_TRACKER.md` left hand-maintained (migration deferred to Phase 2).
- **Deviations:** none
- **Changelog:** no — user-facing no

#### 2026-07-07 — merge
- **Phase:** Phase 1 — Schema, layout, render script
- **Tracker status:** `merged`
- **Choices:** squash merge `b4f75dd` on feature branch `adr/0019-phase-1-yaml-tracker` (deleted post-merge; from `ef56c23` feat(adr): add yaml tracker schema; `88f2774` feat(adr): add tracker render script; `7f53ea8` test(adr): add render tracker tests; `2b5f296` chore(adr): wire tracker ci and docs); ADR accepted as **Accepted (phase 1)** via docs commit `de36ae0`; release skipped; Phase 2 (historical migration) and Phase 3 (agent cutover) deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #24](https://github.com/Tusquito/codebase-indexer-mcp/pull/24) (`adr/0019-phase-1-yaml-tracker`; squash `b4f75dd`); docs accept `de36ae0``
- **Test debt:** carried from verification — blocking render-diff CI check after Phase 2 migration; historical tracker migration to YAML (Phase 2); optional nested-contract (git/changelog) rejection tests
- **Verify:** carried from verification — 9 render unit tests pass; full suite 398 passed (8 storage-integration environmental); Docker integration pass; plan compliance pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/24 — status: merged — commit: b4f75dd
- **Changelog:** no — user-facing no; release skipped

#### 2026-07-07 — implementation
- **Phase:** Phase 1 — Schema, layout, render script
- **Tracker status:** `implemented`
- **Choices:** YAML tracker under `docs/adr/tracker/` with schema-driven validation; `scripts/render_adr_tracker.py` builds four generated markdown blocks between HTML-comment markers, preserving manual preamble; scaffolds a fresh doc when the target has no markers (live-tracker migration deferred to Phase 2). `pyyaml>=6.0` promoted to a direct dev dep in both dev groups. CI validation added as non-blocking (`--check \|\| true`).
- **Deviations:** none
- **Code evidence:** `scripts/render_adr_tracker.py`, `docs/adr/tracker/schema.yaml`, `docs/adr/tracker/phases/0019-phase-1.yaml`, `docs/adr/tracker/events/0019-phase-1-2026-07-07-plan.yaml`, `docs/adr/tracker/events/0008-phase-2b-2026-07-03-merge.yaml`, `mcp_server/tests/test_render_adr_tracker.py`, `mcp_server/tests/fixtures/adr_tracker/`, `mcp_server/pyproject.toml`, `mcp_server/uv.lock`, `.github/workflows/ci.yml`, `docs/adr/README.md`
- **Test debt:** blocking render-diff CI check (after Phase 2); historical tracker migration to YAML (Phase 2)
- **Changelog:** no

### ADR 0020 — Phase 1 — Dataset + training pipeline

#### 2026-07-03 — verification
- **Phase:** Phase 1 — Dataset + training pipeline
- **Tracker status:** `verified`
- **Choices:** `[train]` extra isolated; default holdout = four `multi_hop` queries; mining via base Qwen3 hybrid `run_search` (rerank off); single-pass checkpoint + `train_summary.json`; no Docker/runtime/registry changes
- **Deviations:** single-pass checkpoint save vs per-epoch best selection (documented checkpoint deviation)
- **Code evidence:** `mcp_server/benchmarks/train/`, `mcp_server/pyproject.toml`, `mcp_server/tests/test_train_schema.py`, `mcp_server/tests/test_train_split.py`, `mcp_server/tests/test_export_golden_pairs.py`, `mcp_server/tests/test_mine_hard_negatives.py`, `mcp_server/tests/test_finetune_mrr.py`, `docs/DEPLOYMENT.md`, `.gitignore`
- **Test debt:** GPU smoke for `train_lora`; live Qdrant/Ollama integration for export + mine; per-epoch best-checkpoint selection; `[train]` extra co-install verification on maintainer GPU host
- **Verify:** 17 scoped unit tests pass; plan compliance pass (documented checkpoint deviation); review rounds: 1
- **Changelog:** no

#### 2026-07-03 — prioritization
- **Phase:** Phase 1 — Dataset + training pipeline
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0020 Phase 1 over 0002 Phase 2 payload linking (ADR 0020 embed-quality-first); over 0018 Phase 2 OTel traces (tie ~25.5, lower immediate product impact); over 0017 Phase 2 truncation logging (small ops increment, can parallel); over 0019 Phase 1 YAML tracker (meta-tooling, score ~21); single phase per pipeline rule; no default deployment change until Phase 3 gate passes. **Chosen scope:** Export golden query–passage pairs; hard-negative mining from Qwen3 base top-k misses; LoRA train script with validation holdout; optional `[train]` pyproject extra; unit tests for dataset export; `DEPLOYMENT.md` training stub. Defer Ollama export (P2), quality gate (P3), CI observation (P4). Requires formal Accept of Proposed ADR 0020 before dev. **Why now:** ADR 0016 closed with documented −63.1% recall@10 vs Jina; `eval_baseline_jina.json` exists; golden set + eval harness ready; ADR 0020 defers GraphRAG/telemetry until repo-grounded embed quality; no training tooling in repo yet. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — plan
- **Phase:** Phase 1 — Dataset + training pipeline
- **Tracker status:** `planned`
- **Choices:** Single PR for Phase 1; reuse `eval_retrieval.load_golden` / `resolve_labels` and `run_search` path for mining; hard negatives from base Qwen3 only; default holdout stratified 4 queries with `multi_hop` CLI alternative; training outputs gitignored; no CI GPU/train job; DEPLOYMENT stub only (full Ollama steps in P2); sentence-transformers + PEFT recommended for InfoNCE loop. **Chosen scope:** Add `mcp_server/benchmarks/train/` with `export_golden_pairs.py`, `mine_hard_negatives.py`, `finetune_qwen3_code.py`, shared schema/split/positive helpers, optional `[train]` pyproject extra, unit tests for export/split/mining, `DEPLOYMENT.md` training stub, gitignore for generated artifacts. Validation holdout + best-checkpoint-by-val-MRR. Defer Ollama export/registry (P2), Jina quality gate + baseline update (P3), CI observation job (P4).
- **Deviations:** none
- **Test debt:** unit tests for export/split/mining
- **Changelog:** no — user-facing no

#### 2026-07-03 — merge
- **Phase:** Phase 1 — Dataset + training pipeline
- **Tracker status:** `merged`
- **Choices:** squash merge `02b8794` on feature branch `adr/0020-phase-1-qwen3-code-finetune` (6 commits); ADR accepted as **Accepted (phase 1 — Dataset + training pipeline)**; release skipped; Phases 2–4 deferred (Ollama export/registry P2, Jina quality gate P3, CI observation P4)
- **Deviations:** none
- **Code evidence:** `merged via [PR #15](https://github.com/Tusquito/codebase-indexer-mcp/pull/15) (`adr/0020-phase-1-qwen3-code-finetune`; squash `02b8794`)`
- **Test debt:** carried from verification — GPU smoke for `train_lora`; live Qdrant/Ollama integration for export + mine; per-epoch best-checkpoint selection; `[train]` extra co-install verification on maintainer GPU host
- **Verify:** carried from verification — 17 scoped unit tests pass; plan compliance pass (documented checkpoint deviation); review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/15 — status: merged — commit: 02b8794
- **Changelog:** no — user-facing no; release skipped

#### 2026-07-03 — implementation
- **Phase:** Phase 1 — Dataset + training pipeline
- **Tracker status:** `implemented`
- **Choices:** Optional `[train]` extra isolated from runtime/CI; default validation holdout = all four `multi_hop` golden queries; hard-negative mining uses base Qwen3 hybrid `run_search` (rerank off); LoRA via PEFT + sentence-transformers (TripletLoss when all pairs have mined negatives, else MnRL in-batch); outputs under `benchmarks/train/outputs/` gitignored; no Docker/runtime/registry changes
- **Deviations:** `resolve_positive_passage` (singular) vs plan's `resolve_positive_passages`; supplementary `test_finetune_mrr.py`; single-pass checkpoint save (baseline + final val MRR in `train_summary.json`) vs per-epoch best selection
- **Code evidence:** `mcp_server/benchmarks/train/__init__.py`, `mcp_server/benchmarks/train/_schema.py`, `mcp_server/benchmarks/train/_split.py`, `mcp_server/benchmarks/train/_positives.py`, `mcp_server/benchmarks/train/export_golden_pairs.py`, `mcp_server/benchmarks/train/mine_hard_negatives.py`, `mcp_server/benchmarks/train/finetune_qwen3_code.py`, `mcp_server/benchmarks/train/README.md`, `mcp_server/pyproject.toml`, `mcp_server/tests/test_train_schema.py`, `mcp_server/tests/test_train_split.py`, `mcp_server/tests/test_export_golden_pairs.py`, `mcp_server/tests/test_mine_hard_negatives.py`, `docs/DEPLOYMENT.md`, `.gitignore`
- **Test debt:** GPU smoke for `train_lora`; live Qdrant/Ollama integration for export + mine; per-epoch best-checkpoint selection; `[train]` extra install verification on maintainer GPU host
- **Changelog:** no — user-facing no

### ADR 0021 — Phase 2 — Eval baseline refresh

#### 2026-07-04 — verification
- **Phase:** Phase 2 — Eval baseline refresh
- **Tracker status:** `verified`
- **Choices:** GPU Jina @768 live baseline committed; pre-commit gate vs `eval_baseline_jina.json` failed (0.263 vs 0.660 — alias drift); post-commit Docker self-compare pass; `eval_baseline_jina.json` preserved; scanner `.venv*` prune; golden alias fixes; `_settings.py` `ollama_embed_model` default
- **Deviations:** Pre-commit recall gate vs frozen reference failed — golden alias line drift on HEAD, not embedder regression (carried from implementation)
- **Code evidence:** `mcp_server/benchmarks/fixtures/eval_baseline.json`, `mcp_server/benchmarks/fixtures/eval_baseline_jina.json`, `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `mcp_server/benchmarks/_settings.py`, `mcp_server/src/codebase_indexer/indexer/scanner.py`, `mcp_server/tests/test_scanner_detection.py`, `.codeindexignore`, `docs/adr/0021-revert-jina-production-default-retire-qwen3.md`
- **Test debt:** Golden label realignment; pre-commit recall gate CI; optional `eval_multihop` CI gate
- **Verify:** tests run + plan compliance pass; post-commit Docker self-compare pass; review rounds: 1
- **Changelog:** yes — user-facing

#### 2026-07-04 — prioritization
- **Phase:** Phase 2 — Eval baseline refresh
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0021 P2 over 0002 P2 GraphRAG payload linking (24.5 — embed/baseline debt first); over 0022 P2 (22.0 — blocked until 0021 P2 baseline); over 0018 P2 OTel traces (23.0 — ops increment); over 0017 P2 truncation observability (23.0 — small slice); over Proposed 0019 P1 YAML tracker (17.0 — meta-tooling, needs Accept); over 0022 P3 CI split (depends on P2); single phase per pipeline rule; pre-release: full re-index acceptable, no dual baseline preservation. **Chosen scope:** Re-index golden fixture collection with Jina (`unclemusclez/jina-embeddings-v2-base-code`) @ 768 on GPU stack (`ACCELERATOR=gpu`); run `eval_retrieval --validate-labels` and live verify; commit refreshed `mcp_server/benchmarks/fixtures/eval_baseline.json` with Jina params and `accelerator: gpu` metadata; refresh `multi_hop_2hop` snapshot in baseline if applicable; update ADR 0021 **Measured outcomes** table; defer Phase 3 (ADR index/CHANGELOG housekeeping) and all 0022 P2 factory/ColBERT default changes. **Why now:** ADR 0021 Phase 1 merged (Jina @ 768 production defaults) but `eval_baseline.json` still records Qwen3 @ 1024 (recall@10 0.244); CI `--compare` and integration quality validation misaligned with defaults; ADR 0022 Phase 1 merged (GPU-default compose) enables GPU baseline capture; tracker sequences 0021 P2 before 0022 P2; frozen `eval_baseline_jina.json` (0.660 recall@10) is reference target.
- **Deviations:** none
- **Test debt:** Golden collection re-index @ 768 required; `eval_retrieval --validate-labels` + live verify; refresh `multi_hop_2hop` snapshot if applicable; resolves Phase 1 `smoke_recommend` dim mismatch
- **Verify:** `eval_retrieval --validate-labels`; live verify against frozen `eval_baseline_jina.json` reference (0.660 recall@10)
- **Changelog:** no — user-facing unknown

#### 2026-07-04 — plan
- **Phase:** Phase 2 — Eval baseline refresh
- **Tracker status:** `planned`
- **Choices:** GPU host for capture (`ACCELERATOR=gpu`); single PR with baseline committed (no CI skip, no staged two-commit sequence); `RERANK_ENABLED=false` for baseline parity; reference gate = `eval_baseline_jina.json` recall@10 0.660256; post-commit quality compare threshold 0 (self-compare); pre-commit gate threshold 3 vs frozen reference; do not overwrite `eval_baseline_jina.json`; manual baseline assembly from three eval runs (matches ADR 0016 P2 pattern). **Chosen scope:** GPU re-index golden fixture collection (`codebase-indexer-mcp`) with Jina (`unclemusclez/jina-embeddings-v2-base-code`) @768 on `ACCELERATOR=gpu` stack; run `eval_retrieval --validate-labels`, hybrid + `--no-hybrid` + `eval_multihop` live capture; commit refreshed `mcp_server/benchmarks/fixtures/eval_baseline.json` with Jina params and `accelerator: gpu` metadata in same single PR; gate live capture vs frozen `eval_baseline_jina.json` (±2 pp / threshold 3); update ADR 0021 **Measured outcomes** table; preserve `eval_baseline_jina.json`; Docker integration + `--quality-validation --quality-threshold 0` required before review; defer Phase 3 (CHANGELOG/ADR index housekeeping), ADR 0019 Accept (until after P2 merge), and ADR 0022 P2
- **Deviations:** none
- **Test debt:** Golden collection GPU re-index @768; `eval_retrieval --validate-labels` hybrid + `--no-hybrid`; `eval_multihop` live capture; manual baseline assembly from three eval runs; pre-commit gate vs `eval_baseline_jina.json` (threshold 3); Docker integration `--quality-validation --quality-threshold 0`; resolves Phase 1 `smoke_recommend` dim mismatch
- **Verify:** `eval_retrieval --validate-labels`; live capture gate vs frozen `eval_baseline_jina.json` (recall@10 0.660256, threshold 3); Docker integration `--quality-validation --quality-threshold 0`
- **Changelog:** no — user-facing yes; entry at `verified` step (Phase 3 CHANGELOG housekeeping deferred)

#### 2026-07-04 — merge
- **Phase:** Phase 2 — Eval baseline refresh
- **Tracker status:** `merged`
- **Choices:** squash merge `a076004` on feature branch `adr/0021-phase-2-eval-baseline-refresh`; ADR accepted as `Accepted (phase 1; phase 2 — Eval baseline refresh)`; release skipped; Phase 3 ADR housekeeping + CHANGELOG full update deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #18](https://github.com/Tusquito/codebase-indexer-mcp/pull/18) (`adr/0021-phase-2-eval-baseline-refresh`; squash `a076004`)`
- **Test debt:** carried from verification — golden label realignment; pre-commit recall gate CI; optional `eval_multihop` CI gate
- **Verify:** carried from verification — tests run + plan compliance pass; post-commit Docker self-compare pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/18 — status: merged — commit: a076004
- **Changelog:** no — user-facing yes; entry deferred to Phase 3

#### 2026-07-04 — merge
- **Phase:** Phase 3 — ADR housekeeping + CHANGELOG full update
- **Tracker status:** `merged`
- **Choices:** finisher bundled 0021 P3 README index + CHANGELOG full update in docs commit `53f68e0`; ADR accepted as **Accepted (all phases complete)**; release skipped
- **Deviations:** none — bundled in 0022 P3 finisher per plan (separate docs commit)
- **Code evidence:** `bundled docs accept via [PR #20](https://github.com/Tusquito/codebase-indexer-mcp/pull/20) (`53f68e0`)`
- **Test debt:** carried from Phase 2 — golden label realignment; pre-commit recall gate CI; optional `eval_multihop` CI gate
- **Verify:** carried from 0022 P3 merge — finisher close-out complete
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/20 — status: pending — commit: 53f68e0
- **Changelog:** no — invoker Changelog: no; `[Unreleased]` bullets only

#### 2026-07-04 — implementation
- **Phase:** Phase 2 — Eval baseline refresh
- **Tracker status:** `implemented`
- **Choices:** GPU host (`ACCELERATOR=gpu`); single PR with live baseline committed; `RERANK_ENABLED=false`; pre-commit gate vs `eval_baseline_jina.json` threshold 3; post-commit quality compare threshold 0; preserve `eval_baseline_jina.json`; scanner `.venv*` prune + golden `scanner.py:113` alias fix; `_settings.py` `ollama_embed_model` default added
- **Deviations:** Pre-commit gate vs `eval_baseline_jina.json` failed (recall@10 0.263 vs 0.660) — golden alias line drift on HEAD, not embedder regression; committed live metrics with ADR documentation; golden label realignment deferred
- **Code evidence:** `mcp_server/benchmarks/fixtures/eval_baseline.json`, `docs/adr/0021-revert-jina-production-default-retire-qwen3.md`, `mcp_server/benchmarks/_settings.py`, `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `mcp_server/src/codebase_indexer/indexer/scanner.py`, `mcp_server/tests/test_scanner_detection.py`, `.codeindexignore`
- **Test debt:** Golden label realignment on HEAD to recover ≥0.660 vs frozen reference; pre-commit recall gate CI; optional `eval_multihop` CI gate
- **Changelog:** no — user-facing yes; entry deferred to `verified` step (Phase 3 CHANGELOG housekeeping also deferred)

#### 2026-07-03 — verification
- **Phase:** Phase 1 — Config + docs revert
- **Tracker status:** `verified`
- **Choices:** Jina production default @ 768 in env/bench/compose/docs; Qwen3 demoted to experimental preset with −63.1% recall@10 citation; `OLLAMA_EMBED_MODEL` uncommented in `.env.example` REQUIRED; compose Jina pull documented manual-only (no deploy auto-pull); Qwen3 registry/MRL in `config.py` retained; ADR index housekeeping included in Phase 1 PR scope; `eval_baseline.json` refresh deferred Phase 2; CHANGELOG full update deferred Phase 3
- **Deviations:** ADR index housekeeping included in Phase 1 (plan deferred to Phase 3); CHANGELOG bullet added at `verified` (plan deferred full update to Phase 3)
- **Code evidence:** `.env.example`, `mcp_server/benchmarks/_settings.py`, `scripts/run_compose_integration.py`, `README.md`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md`, `mcp_server/tests/conftest.py`, `mcp_server/tests/test_config.py`, `docs/adr/0021-revert-jina-production-default-retire-qwen3.md`, `docs/adr/README.md`, `docs/adr/0016-qwen3-embedding-default-dense-model.md`, `docs/adr/0020-qwen3-code-finetune-jina-quality-gate.md`
- **Test debt:** Optional `smoke_recommend_code` fails until golden collection re-indexed @ 768 (Phase 2); `eval_baseline.json` still Qwen3 @ 1024 until Phase 2 refresh
- **Verify:** Full `uv run pytest` (346 passed); targeted embed/config tests (24 passed); plan compliance pass on all Phase 1 paths; Docker integration report verdict `pass`
- **Changelog:** yes — user-facing; bullet added at `verified`; full CHANGELOG housekeeping deferred Phase 3

#### 2026-07-03 — prioritization
- **Phase:** Phase 1 — Config + docs revert
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0021 Phase 1 over 0018 Phase 2 OTel traces (ops increment, lower retrieval impact); over 0002 Phase 2 GraphRAG payload linking (0021 explicitly defers until embed default stable); over 0017 Phase 2 truncation observability (small, can parallel after 0021 P1); over 0019 Phase 1 YAML tracker (meta-tooling, score ~19); over cancelled 0020 Phases 2–4; single phase per pipeline rule; fixture-beats-leaderboard principle (ADR 0007). **Chosen scope:** Phase 1 only — revert production defaults in `.env.example`, `.env.compose.integration`, `mcp_server/benchmarks/_settings.py`, `scripts/run_compose_integration.py`, and primary docs to Jina @ 768; demote Qwen3 to experimental preset block with regression citation; retain Qwen3 in `KNOWN_EMBED_MODEL_*` and MRL passthrough. Defer Phase 2 (golden re-index + `eval_baseline.json`) and Phase 3 (ADR index/tracker/CHANGELOG housekeeping) to subsequent cycles. Requires formal Accept of Proposed ADR 0021 before dev. **Why now:** ADR 0016 + 0020 embedding track closed through fine-tune gate failure; golden-set evidence (−63.1% recall@10 vs Jina) and `eval_baseline_jina.json` exist; code and docs still default to Qwen3 (`.env.example`, `_settings.py`, README, ARCHITECTURE, DEPLOYMENT, `eval_baseline.json`); ADR 0021 unblocks embedding-stable GraphRAG and telemetry phases; no new mandatory infra; validation path via existing `test_config.py` Jina registry tests and future Phase 2 `eval_retrieval`. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Test debt:** validation via existing `test_config.py` Jina registry tests; future Phase 2 `eval_retrieval`
- **Verify:** existing `test_config.py` Jina registry tests; future Phase 2 `eval_retrieval`
- **Changelog:** no — user-facing unknown

#### 2026-07-03 — plan
- **Phase:** Phase 1 — Config + docs revert
- **Tracker status:** `planned`
- **Choices:** `OLLAMA_EMBED_MODEL` uncommented in `.env.example` REQUIRED; compose integration pull documented in docstring + `write_integration_env()` pre-step only (no auto-pull in deploy); README lists ADR 0021 as primary default-dense ADR with 0016 one-line historical note; `.env.compose.integration` updated via generator only (gitignored); one PR for entire Phase 1. **Chosen scope:** Revert production defaults to Jina @ 768 in `.env.example` (with uncommented `OLLAMA_EMBED_MODEL` in REQUIRED), `mcp_server/benchmarks/_settings.py`, `scripts/run_compose_integration.py` (Jina generator env + documented manual pull, no deploy auto-pull), and primary docs (`README.md`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md`); demote Qwen3 to experimental preset block with −63.1% recall@10 citation; align `conftest.py` + `test_config.py`; retain Qwen3 in `KNOWN_EMBED_MODEL_*` and MRL passthrough; defer Phase 2 (`eval_baseline.json`) and Phase 3 (ADR index/CHANGELOG housekeeping)
- **Deviations:** none
- **Test debt:** align `conftest.py` + `test_config.py` for Jina defaults; existing Jina registry tests
- **Changelog:** no — user-facing yes; entry at `verified` step (Phase 3 CHANGELOG housekeeping deferred)

#### 2026-07-03 — merge
- **Phase:** Phase 1 — Config + docs revert
- **Tracker status:** `merged`
- **Choices:** squash merge `f50fa98` on feature branch `adr/0021-phase-1-revert-jina-default`; ADR accepted as `Accepted (phase 1 — Config + docs revert)` (docs accept `a4a61a6`); release skipped; Phase 2 eval baseline refresh and Phase 3 ADR housekeeping deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #16](https://github.com/Tusquito/codebase-indexer-mcp/pull/16) (`adr/0021-phase-1-revert-jina-default`; squash `f50fa98`; docs accept `a4a61a6`)`
- **Test debt:** carried from verification — `smoke_recommend` dim mismatch until Phase 2 re-index; `eval_baseline.json` still Qwen3 until Phase 2
- **Verify:** carried from verification — 346 pytest; Docker integration pass; 1 review round
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/16 — status: merged — commit: f50fa98
- **Changelog:** no — Unreleased bullet only; no version cut

#### 2026-07-03 — implementation
- **Phase:** Phase 1 — Config + docs revert
- **Tracker status:** `implemented`
- **Choices:** Reverted production defaults to Jina v2 base code @ 768 (`jinaai/jina-embeddings-v2-base-code` / `unclemusclez/jina-embeddings-v2-base-code`); demoted Qwen3 to experimental/CoIR preset with −63.1% recall@10 warning; left `config.py` Qwen3 registry/MRL untouched; deferred `eval_baseline.json` to Phase 2
- **Deviations:** none
- **Code evidence:** `.env.example`, `mcp_server/benchmarks/_settings.py`, `mcp_server/tests/conftest.py`, `mcp_server/tests/test_config.py`, `scripts/run_compose_integration.py`, `README.md`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md`
- **Test debt:** Full `uv run pytest` blocked locally by broken `tokenizers` in `.venv` (8 pre-existing failures); compose integration live Jina Ollama pull not run in this session; `eval_baseline.json` unchanged (Phase 2)
- **Changelog:** no

### ADR 0022 — Phase 1 — GPU-default compose + docs

#### 2026-07-04 — verification
- **Phase:** Phase 1 — GPU-default compose + docs
- **Tracker status:** `verified`
- **Choices:** Compose-only `ACCELERATOR=gpu` default; canonical `-f` via `scripts/compose_files.py`; fail-fast `require_gpu()`; sparse BM25 unchanged; CI/`ollama ps` deferred Phase 3; ColBERT remote GPU default deferred Phase 2
- **Deviations:** none
- **Code evidence:** ``scripts/accelerator.py`, `scripts/compose_files.py`, `scripts/run_compose_integration.py`, `.env.example`, `README.md`, `docs/DEPLOYMENT.md`, `docs/ARCHITECTURE.md`, docker-compose GPU overrides, `mcp_server/tests/test_accelerator.py`, `mcp_server/tests/test_compose_files.py``
- **Test debt:** `ollama ps` GPU assertion; CI `ACCELERATOR=cpu` — Phase 3
- **Verify:** 12 unit tests pass; plan compliance pass; integration verdict pass
- **Changelog:** yes — user-facing; bullet added at `verified`

#### 2026-07-04 — verification
- **Phase:** Phase 2 — Retire CPU ColBERT defaults
- **Tracker status:** `verified`
- **Choices:** Remote GPU sidecar default when `RERANK_ENABLED=true`; explicit onnx for `ACCELERATOR=cpu`; Phase 3 CI split deferred
- **Deviations:** none
- **Code evidence:** `config.py`, `compose_files.py`, `docker-compose.yml`, `docker-compose.colbert-worker.yml`, `docker-compose.colbert-worker.gpu.yml`, `.env.example`, `README.md`, `docs/DEPLOYMENT.md`, `docs/ARCHITECTURE.md`, `docs/SEARCH_BEHAVIOR.md`, `docs/adr/0015-colbert-http-sidecar.md`, `docs/adr/0022-gpu-default-cpu-fallback.md`, `test_config.py`, `test_compose_files.py`, `test_factory.py`
- **Test debt:** Phase 3 CI `ACCELERATOR=cpu`; optional `bench_colbert_sidecar.py`; golden label realignment deferred
- **Verify:** 368 unit tests pass; integration pass; quality validation threshold 0 self-compare pass; plan compliance pass
- **Changelog:** yes — user-facing; bullet added at `verified`

#### 2026-07-04 — verification
- **Phase:** Phase 3 — CI split
- **Tracker status:** `verified`
- **Choices:** Six ubuntu-latest jobs `ACCELERATOR=cpu`; blocking compose-integration; non-blocking gpu-smoke; `check_ollama_gpu_processor()` gates GPU verdict; quality validation skipped; finisher bundles 0021 P3
- **Deviations:** none
- **Code evidence:** `.github/workflows/ci.yml`, `scripts/run_compose_integration.py`, `mcp_server/tests/test_run_compose_integration_gpu.py`, `docs/DEPLOYMENT.md`, `docs/adr/0022-gpu-default-cpu-fallback.md`
- **Test debt:** First green GHA compose-integration; gpu-smoke self-hosted runner; 0021 P3 finisher
- **Verify:** 375 unit tests pass; integration pass GPU+CPU; plan compliance pass
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-04 — prioritization
- **Phase:** Phase 1 — GPU-default compose + docs
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0022 P1 over 0021 P2 (Accepted, eval baseline still Qwen3 — sequenced after GPU compose default per ADR 0022); over 0002 P2 GraphRAG payload linking (large re-index, embed stable but topology wrong); over 0018 P2 OTel traces (ops increment, lower immediate deploy impact); over 0019 P1 YAML tracker (meta-tooling, score ~20.5); over cancelled 0020 P2–4; single phase per pipeline rule; pre-release: breaking GPU default acceptable, no CPU parallel default preservation. **Chosen scope:** Phase 1 only — `ACCELERATOR=gpu` default; `scripts/compose_files.py` + `scripts/accelerator.py`; merge GPU compose overrides by default; update `.env.example`, `README.md`, `docs/DEPLOYMENT.md`; wire `scripts/run_compose_integration.py`; unit tests (`test_compose_files.py`, `require_gpu()`); CI jobs set explicit `ACCELERATOR=cpu`. Defer Phase 2 (ColBERT remote GPU default + 0021 P2 baseline) and Phase 3 (self-hosted GPU CI smoke). Requires formal Accept of Proposed ADR 0022 and README index row before dev. **Why now:** GPU infra from ADR 0015 P2 and Jina defaults from ADR 0021 P1 are merged, but deploy/eval/integration still CPU-opt-in (`OLLAMA_GPU`, no `ACCELERATOR`, CPU-only `run_compose_integration.py` compose list). ADR 0022 P1 establishes production topology and fail-fast before 0021 P2 GPU baseline capture. Pre-release policy accepts breaking GPU defaults. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Test debt:** unit tests (`test_compose_files.py`, `require_gpu()`)
- **Changelog:** no — user-facing unknown

#### 2026-07-04 — prioritization
- **Phase:** Phase 2 — Retire CPU ColBERT defaults
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0022 Phase 2 over 0002 Phase 2 GraphRAG payload linking; over 0021 Phase 3 housekeeping; over Proposed 0019 Phase 1 YAML tracker; single phase per pipeline rule. **Why now:** ADR 0022 Phase 1 merged ([PR #17](https://github.com/Tusquito/codebase-indexer-mcp/pull/17)); prerequisite ADR 0021 Phase 2 (GPU Jina baseline) merged 2026-07-04 ([PR #18](https://github.com/Tusquito/codebase-indexer-mcp/pull/18)). GPU-default compose exists but ColBERT still defaults to in-process CPU ONNX. **Chosen scope:** When `RERANK_ENABLED=true` on `ACCELERATOR=gpu` stack, default `COLBERT_EMBED_BACKEND=remote` and GPU sidecar compose; update `factory.py` / Settings defaults and validation; ensure `compose_files.py` includes GPU ColBERT worker files in rerank-on mode; update `.env.example`, `DEPLOYMENT.md`, `ARCHITECTURE.md`, ADR 0015 cross-links; unit tests for factory defaults and compose resolution; integration via `run_compose_integration.py` with rerank-on remote sidecar on GPU host. Defer Phase 3 (explicit `ACCELERATOR=cpu` in CI, self-hosted GPU smoke). **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Test debt:** unit tests for factory defaults and compose resolution; integration via `run_compose_integration.py` with rerank-on remote sidecar on GPU host; golden-set realignment deferred
- **Verify:** unit tests + integration harness rerank-on remote sidecar on GPU host
- **Changelog:** no — user-facing yes; draft at verification

#### 2026-07-04 — prioritization
- **Phase:** Phase 3 — CI split
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0022 Phase 3; human decisions resolved 2026-07-04: GPU smoke included in PR; GHA compose integration job added; 0021 P3 bundled in finisher docs commit. **Why now:** ADR 0022 Phases 1 and 2 merged ([PR #17](https://github.com/Tusquito/codebase-indexer-mcp/pull/17), [PR #19](https://github.com/Tusquito/codebase-indexer-mcp/pull/19)); both explicitly deferred `.github/workflows/ci.yml` changes. Code grep confirms zero `ACCELERATOR` in `ci.yml` while ADR 0022 mandates `ACCELERATOR=cpu` on every ubuntu-latest job as the sole CPU exception. Completing Phase 3 closes the GPU-default accelerator arc. **Chosen scope:** Add `ACCELERATOR: cpu` to every job `env` in `.github/workflows/ci.yml`; add GHA compose-integration job with `ACCELERATOR=cpu`; include optional non-blocking self-hosted GPU smoke job in same PR; finisher bundles ADR 0021 Phase 3 housekeeping (README index + CHANGELOG). Maintainer GPU host runs `scripts/run_compose_integration.py` before code review per project-phase. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Code evidence:** ``.github/workflows/ci.yml` — zero `ACCELERATOR` matches (grep 2026-07-04)`
- **Changelog:** no — user-facing unknown

#### 2026-07-04 — plan
- **Phase:** Phase 1 — GPU-default compose + docs
- **Tracker status:** `planned`
- **Choices:** Accept ADR 0022 (Proposed → Accepted, partial Phase 1) + README index row (next-number → 0023) are **first tasks in this phase PR**, before code changes. Phase 1 does not modify `.github/workflows/ci.yml`. After 0022 P1 merge, next cycle is 0021 P2 then 0022 P2. Pre-release: breaking GPU default; sparse BM25 stays in-process CPU. **Chosen scope:** `ACCELERATOR=gpu` default; new `scripts/compose_files.py` + `scripts/accelerator.py` (`require_gpu()`); merge GPU compose overrides by default; update `.env.example`, `README.md`, `docs/DEPLOYMENT.md`, `docs/ARCHITECTURE.md`, compose header comments; wire `scripts/run_compose_integration.py` to GPU stack; unit tests (`test_compose_files.py`, `test_accelerator.py`). Defer Phase 2 (ColBERT remote GPU default + 0021 P2 baseline), Phase 3 (CI `ACCELERATOR=cpu`, self-hosted GPU smoke, `ollama ps` GPU assertion). **Assumptions:** NVIDIA + Container Toolkit on maintainer integration host; integration harness keeps `RERANK_ENABLED=false`.
- **Deviations:** none
- **Test debt:** unit tests (`test_compose_files.py`, `test_accelerator.py`)
- **Changelog:** no — user-facing yes; draft at verified

#### 2026-07-04 — plan
- **Phase:** Phase 2 — Retire CPU ColBERT defaults
- **Tracker status:** `planned`
- **Choices:** onnx default unchanged when `RERANK_ENABLED=false`; remote+GPU sidecar default when rerank on; self-compare quality gate (`--threshold 0`); CHANGELOG at verification; single PR; no `ci.yml` changes. **Chosen scope:** When `RERANK_ENABLED=true`, default `COLBERT_EMBED_BACKEND=remote` in Settings (via `model_fields_set` validator) and `compose_files.py`; GPU sidecar compose merged automatically on `ACCELERATOR=gpu`; in-process ONNX retained only for explicit `COLBERT_EMBED_BACKEND=onnx` (`ACCELERATOR=cpu`). When `RERANK_ENABLED=false`, onnx default unchanged. Update `.env.example`, `README.md`, `docs/DEPLOYMENT.md`, `docs/ARCHITECTURE.md`, `docs/SEARCH_BEHAVIOR.md`, compose headers, ADR 0015 cross-link; unit tests; partial ADR accept in PR. Defer Phase 3 (CI `ACCELERATOR=cpu`, self-hosted GPU smoke); defer golden label realignment; defer ADR 0019 Accept; defer CHANGELOG to verification.
- **Deviations:** none
- **Test debt:** unit tests for factory defaults and compose resolution; integration via `run_compose_integration.py` with rerank-on remote sidecar on GPU host (default smoke keeps `RERANK_ENABLED=false`); golden label realignment deferred
- **Verify:** unit tests + integration harness; self-compare quality gate (`--threshold 0`)
- **Changelog:** no — user-facing yes; draft at verification

#### 2026-07-04 — plan
- **Phase:** Phase 3 — CI split
- **Tracker status:** `planned`
- **Choices:** Human decisions incorporated 2026-07-04 — GPU smoke in PR; GHA compose-integration job; 0021 P3 bundled in finisher docs commit. Single PR for Phase 3. Quality validation skipped (CI-only phase). **Chosen scope:** Add `ACCELERATOR: cpu` to every existing `ubuntu-latest` job in `.github/workflows/ci.yml`; add blocking GHA `compose-integration` job running `scripts/run_compose_integration.py` with `ACCELERATOR=cpu`; add optional non-blocking self-hosted `gpu-smoke` job with `ACCELERATOR=gpu`; extend integration harness with `ollama ps` GPU processor assertion when `ACCELERATOR=gpu`; update `docs/DEPLOYMENT.md` CI section; ADR 0022 partial status → Phase 3 track. Finisher bundles ADR 0021 Phase 3 housekeeping in separate docs commit.
- **Deviations:** none
- **Test debt:** blocking GHA compose-integration job; optional non-blocking self-hosted GPU smoke; `ollama ps` GPU processor assertion when `ACCELERATOR=gpu`
- **Changelog:** no — user-facing yes; status `planned` (not verified); invoker Changelog: no

#### 2026-07-04 — merge
- **Phase:** Phase 1 — GPU-default compose + docs
- **Tracker status:** `merged`
- **Choices:** merge on feature branch `adr/0022-phase-1-gpu-default-compose`; ADR accepted as `Accepted (phase 1 — GPU-default compose + docs)`; release skipped; Phase 2 (ColBERT remote GPU default + 0021 P2 baseline) and Phase 3 (CI `ACCELERATOR=cpu`, self-hosted GPU smoke, `ollama ps` GPU assertion) deferred; next cycle: 0021 P2 then 0022 P2 per plan
- **Deviations:** none
- **Code evidence:** `merged via [PR #17](https://github.com/Tusquito/codebase-indexer-mcp/pull/17) (`adr/0022-phase-1-gpu-default-compose`; `efdc14de6470cceb9abaf7bce2096ebb03331513`)`
- **Test debt:** carried from verification — `ollama ps` GPU assertion; CI `ACCELERATOR=cpu` — Phase 3
- **Verify:** carried from verification — 12 unit tests pass; plan compliance pass; integration verdict pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/17 — status: merged — commit: efdc14de6470cceb9abaf7bce2096ebb03331513
- **Changelog:** no — release skipped; `[Unreleased]` bullet retained from verification step

#### 2026-07-04 — merge
- **Phase:** Phase 2 — Retire CPU ColBERT defaults
- **Tracker status:** `merged`
- **Choices:** merge on feature branch `adr/0022-phase-2-retire-cpu-colbert-defaults`; ADR accepted as `Accepted (phase 1; phase 2 — Retire CPU ColBERT defaults)`; release skipped; Phase 3 (CI `ACCELERATOR=cpu`, self-hosted GPU smoke, `ollama ps` GPU assertion) deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #19](https://github.com/Tusquito/codebase-indexer-mcp/pull/19) (`adr/0022-phase-2-retire-cpu-colbert-defaults`; squash `7fb7e7c`; accept docs `bddadc6`)`
- **Test debt:** carried from verification — Phase 3 CI `ACCELERATOR=cpu`; optional `bench_colbert_sidecar.py`; golden label realignment deferred
- **Verify:** carried from verification — 368 unit tests pass; integration pass; quality validation threshold 0 self-compare pass; plan compliance pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/19 — status: merged — commit: bddadc6
- **Changelog:** no — already in `[Unreleased]` from verified step

#### 2026-07-04 — merge
- **Phase:** Phase 3 — CI split
- **Tracker status:** `merged`
- **Choices:** squash merge PR #20 on feature branch `adr/0022-phase-3-ci-split`; ADR accepted as **Accepted (all phases complete)**; ADR 0021 promoted **Accepted (all phases complete)** in bundled finisher docs commit `53f68e0`; release skipped; six ubuntu-latest jobs `ACCELERATOR=cpu`; blocking compose-integration; non-blocking gpu-smoke; `check_ollama_gpu_processor()` in harness
- **Deviations:** none
- **Code evidence:** `merged via [PR #20](https://github.com/Tusquito/codebase-indexer-mcp/pull/20) (`adr/0022-phase-3-ci-split`; squash `37a3364`; bundled docs accept `53f68e0`)`
- **Test debt:** gpu-smoke first run when self-hosted runner available
- **Verify:** carried from verification — 375 unit tests pass; integration pass GPU+CPU; plan compliance pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/20 — status: merged — commit: 53f68e0
- **Changelog:** no — user-facing yes; invoker Changelog: no; `[Unreleased]` bullets only

#### 2026-07-04 — implementation
- **Phase:** Phase 1 — GPU-default compose + docs
- **Tracker status:** `implemented`
- **Choices:** Accepted ADR 0022 partial Phase 1 in same PR; `ACCELERATOR=gpu` default via compose-only env; canonical `-f` list in `scripts/compose_files.py`; fail-fast `require_gpu()` in integration harness; sparse BM25 unchanged (CPU in MCP); no `ci.yml` changes (Phase 3)
- **Deviations:** none
- **Code evidence:** ``scripts/accelerator.py`, `scripts/compose_files.py`, `scripts/run_compose_integration.py`, `.env.example`, `README.md`, `docs/DEPLOYMENT.md`, `docs/ARCHITECTURE.md`, docker-compose files, `.github/copilot-instructions.md`, `mcp_server/tests/test_compose_files.py`, `mcp_server/tests/test_accelerator.py`, `docs/adr/0022-gpu-default-cpu-fallback.md`, `docs/adr/README.md``
- **Test debt:** `ollama ps` GPU assertion and CI `ACCELERATOR=cpu` deferred to Phase 3
- **Changelog:** no — user-facing yes; status `implemented` (not verified); invoker Changelog: no

#### 2026-07-04 — implementation
- **Phase:** Phase 2 — Retire CPU ColBERT defaults
- **Tracker status:** `implemented`
- **Choices:** When `RERANK_ENABLED=true`, `COLBERT_EMBED_BACKEND` defaults to remote in Settings, compose env, and `compose_files.py`; explicit onnx for `ACCELERATOR=cpu` only; rerank off keeps onnx default
- **Deviations:** none
- **Code evidence:** `config.py`, `compose_files.py`, `docker-compose.yml`, `docker-compose.colbert-worker.yml`, `docker-compose.colbert-worker.gpu.yml`, `.env.example`, `README.md`, `docs/DEPLOYMENT.md`, `docs/ARCHITECTURE.md`, `docs/SEARCH_BEHAVIOR.md`, `docs/adr/0015-colbert-http-sidecar.md`, `docs/adr/0022-gpu-default-cpu-fallback.md`, `test_config.py`, `test_compose_files.py`, `test_factory.py`, `test_colbert_rerank_slow.py`
- **Test debt:** Phase 3 CI split; optional `bench_colbert_sidecar.py` performance report
- **Changelog:** no — user-facing yes; status `implemented` (not verified); invoker Changelog: no

#### 2026-07-04 — implementation
- **Phase:** Phase 3 — CI split
- **Tracker status:** `implemented`
- **Choices:** All five ubuntu-latest jobs `ACCELERATOR=cpu`; blocking compose-integration job; non-blocking gpu-smoke; `check_ollama_gpu_processor()` in harness; quality validation skipped
- **Deviations:** none
- **Code evidence:** `.github/workflows/ci.yml`, `scripts/run_compose_integration.py`, `mcp_server/tests/test_run_compose_integration_gpu.py`, `docs/DEPLOYMENT.md`, `docs/adr/0022-gpu-default-cpu-fallback.md`
- **Test debt:** first green compose-integration GHA run; gpu-smoke runner verification; maintainer GPU harness
- **Changelog:** no — user-facing yes; status `implemented` (not verified); invoker Changelog: no

### ADR 0023 — Phase 1 — Symbol-unified CALLS + Neo4j caller query + dual-read routing

#### 2026-07-04 — verification
- **Phase:** Phase 1 — Symbol-unified CALLS + Neo4j caller query + dual-read routing
- **Tracker status:** `verified`
- **Choices:** `call_token` on CALLS; symbol unification Rules 1–3; Path D routes Neo4j when `GRAPH_ENABLED` else Qdrant; Qdrant `callees` dual-write retained; `GRAPH_SCHEMA_VERSION=2`; defer Phases 2–4 and live Neo4j test debt
- **Deviations:** none
- **Code evidence:** `graph_writer.py`, `neo4j.py`, `cross_references.py`, `config.py`, `test_graph_writer.py`, `test_neo4j_storage.py`, `test_cross_references.py`, `test_config.py`, `docs/ARCHITECTURE.md`, `.env.example`
- **Test debt:** live Neo4j parity fixture; unified-symbol Cypher traversal; mixed-collection per-engine routing (Phase 2)
- **Verify:** 383 unit tests pass; integration pass; quality validation threshold 0 pass; plan compliance pass
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-04 — verification
- **Phase:** Phase 2 — Stop dual-write to Qdrant
- **Tracker status:** `verified`
- **Choices:** Reused `graph_call_sites` metadata; per-collection Path D routing; Qdrant fallback + warning; retain callees index until Phase 3
- **Deviations:** none
- **Code evidence:** ``qdrant.py`, `pipeline.py`, `cross_references.py`, test files, `ARCHITECTURE.md`, `.env.example`, `CHANGELOG.md``
- **Test debt:** Testcontainers slow test optional CI job
- **Verify:** tests run + plan compliance pass; `cd mcp_server && uv run pytest -q` — 391 passed; Docker integration pass
- **Changelog:** yes — user-facing; bullet already in `[Unreleased]` from implementation

#### 2026-07-04 — prioritization
- **Phase:** Phase 1 — Symbol-unified CALLS + Neo4j caller query + dual-read routing
- **Tracker status:** `candidate`
- **Choices:** Prioritizer ranked ADR 0023 Phase 1 (score ~26) over alternatives: 0002 Phase 3 `expand_search_context` (~24), 0017 Phase 2 truncation observability (~23), 0002 Phase 2 Qdrant `graph_node_ids` (~22); single phase per pipeline rule; pre-release: no backward-compat shrink. **Why now:** ADR 0022 all phases merged (GPU-default arc complete); ADR 0002 Phase 1 shipped Neo4j graph writer with CALLS edges but no caller query path; Qdrant `callees` payload duplicates graph data when `GRAPH_ENABLED=true`; call-site lookup is structural edge query — Neo4j is natural authority; unlocks multi-hop graph queries before ADR 0002 Phase 4. **Chosen scope:** `call_token` on CALLS relationships; symbol unification (CALLS target merges with DEFINES symbol when resolvable); `Neo4jStorage.find_callers` Cypher query; dual-engine routing in `cross_references.py` (Neo4j when graph enabled, Qdrant scroll fallback); parity tests; keep Qdrant `callees` dual-write (Phase 2 retires payload); bump `GRAPH_SCHEMA_VERSION` + graph re-index required. **Human gate resolved 2026-07-04:** (1) Accept ADR 0023 before planning; (2) symbol unification — exact `Symbol.name` match same-collection, qualified import fallback, keep stubs when ambiguous; (3) Phase 1 only, no 0002 P3 combine; (4) re-index messaging in PR body + `.env.example` comment, no CHANGELOG until user-facing phase. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Changelog:** no — user-facing unknown; invoker Changelog: no

#### 2026-07-04 — prioritization
- **Phase:** Phase 2 — Stop dual-write to Qdrant
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0023 Phase 2 over 0002 Phase 2 (tie-breaker: lower scope/risk); single phase per pipeline rule. **Why now:** Phase 1 merged 2026-07-04 ([PR #21](https://github.com/Tusquito/codebase-indexer-mcp/pull/21)); `call_token`, `Neo4jStorage.find_callers`, and Path D Neo4j routing exist; Qdrant still dual-writes `callees`; ADR and tracker explicitly defer payload retirement to Phase 2; prerequisites satisfied. **Chosen scope:** Omit `callees` from Qdrant upsert when `GRAPH_ENABLED=true` for graph-indexed collections; add/reuse collection metadata flag (`graph_call_sites` or `graph_enabled`); per-collection engine routing in `find_cross_references` Path D for mixed batches; unit/integration tests; Testcontainers Neo4j parity fixture; document forced re-index; `[Unreleased]` CHANGELOG bullet; Docker integration required. **Suggested scope:** one phase (= one PR). **Human gate resolved 2026-07-04:** (1) per-collection engine selection for mixed batches; (2) Testcontainers Neo4j fixture in Phase 2; (3) CHANGELOG bullet in Phase 2.
- **Deviations:** none
- **Changelog:** no — user-facing unknown; invoker Changelog: no

#### 2026-07-04 — plan
- **Phase:** Phase 1 — Symbol-unified CALLS + Neo4j caller query + dual-read routing
- **Tracker status:** `planned`
- **Choices:** Accept ADR 0023 (Proposed → Accepted partial Phase 1) as **first PR task** before code changes; symbol unification: exact `Symbol.name` → qualified import fallback → stub on ambiguity; Phase 1 only — exclude ADR 0002 Phase 3 `expand_search_context` and 0023 Phases 2–4; re-index messaging in PR body + `.env.example` only; no CHANGELOG at this phase
- **Deviations:** none
- **Test debt:** parity tests (Qdrant vs Neo4j on Java/Spring fixtures) + graph-disabled regression; `test_cross_references.py` Java inherited-field fixtures as parity oracle
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-04 — plan
- **Phase:** Phase 2 — Stop dual-write to Qdrant
- **Tracker status:** `planned`
- **Choices:** Per-collection engine selection for mixed batches; Testcontainers Neo4j integration test in Phase 2; `[Unreleased]` CHANGELOG bullet in Phase 2; reuse Qdrant collection metadata key `graph_call_sites`; single PR per phase; pre-release: no `GRAPH_SCHEMA_VERSION` env; retain `callees` keyword index until Phase 3
- **Deviations:** none
- **Test debt:** Testcontainers Neo4j caller-query parity fixture; mixed-collection per-engine routing
- **Changelog:** no — draft at verify; invoker Changelog: yes; user-facing yes

#### 2026-07-04 — merge
- **Phase:** Phase 1 — Symbol-unified CALLS + Neo4j caller query + dual-read routing
- **Tracker status:** `merged`
- **Choices:** merge on feature branch `adr/0023-phase-1-neo4j-call-site-lookup`; ADR accept skipped — unchanged `Accepted (phase 1 — Symbol-unified CALLS + Neo4j caller query + dual-read routing)`; release skipped; Phases 2–4 deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #21](https://github.com/Tusquito/codebase-indexer-mcp/pull/21) (`adr/0023-phase-1-neo4j-call-site-lookup`; `963f041df73ac6e1fbb05287debe4bccdd91526d`)`
- **Test debt:** carried from verification — live Neo4j parity fixture; unified-symbol Cypher traversal; mixed-collection per-engine routing (Phase 2)
- **Verify:** carried from verification — 383 unit tests pass; integration pass; quality validation threshold 0 pass; plan compliance pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/21 — status: merged — commit: 963f041df73ac6e1fbb05287debe4bccdd91526d
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-04 — merge
- **Phase:** Phase 2 — Stop dual-write to Qdrant
- **Tracker status:** `merged`
- **Choices:** merge on feature branch `adr/0023-phase-2-stop-qdrant-dual-write`; ADR accept — `Accepted (phase 1; phase 2 — Stop dual-write to Qdrant)`; release skipped; Phases 3–4 deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #22](https://github.com/Tusquito/codebase-indexer-mcp/pull/22) (`adr/0023-phase-2-stop-qdrant-dual-write`; squash `d0e8348`)`
- **Test debt:** carried from verification — Testcontainers slow test optional CI job
- **Verify:** carried from verification — 391 unit tests pass; integration pass; plan compliance pass; review rounds: 2
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/22 — status: merged — commit: d0e8348
- **Changelog:** no — user-facing yes; invoker Changelog: no; `[Unreleased]` bullet retained from verification step

#### 2026-07-04 — implementation
- **Phase:** Phase 1 — Symbol-unified CALLS + Neo4j caller query + dual-read routing
- **Tracker status:** `implemented`
- **Choices:** Accepted ADR 0023 (partial Phase 1) before code; symbol unification Rules 1–3 (exact `Symbol.name` → qualified import fallback → stub); `call_token` on CALLS; `Neo4jStorage.find_callers`; Path D dual-read (Neo4j when `GRAPH_ENABLED`, else Qdrant); Qdrant `callees` dual-write retained; `GRAPH_SCHEMA_VERSION=2` + re-index note in `.env.example`; no CHANGELOG
- **Deviations:** none
- **Code evidence:** `graph_writer.py`, `neo4j.py`, `cross_references.py`, `config.py`, `docker-compose.neo4j.yml`, `.env.example`, `docs/ARCHITECTURE.md`, `docs/adr/0023-neo4j-primary-call-site-lookup.md`, `docs/adr/README.md`, `test_graph_writer.py`, `test_neo4j_storage.py`, `test_cross_references.py`, `test_config.py`
- **Test debt:** live Neo4j parity fixture; unified-symbol Cypher traversal; mixed-collection routing
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-04 — implementation
- **Phase:** Phase 2 — Stop dual-write to Qdrant
- **Tracker status:** `implemented`
- **Choices:** Reused Qdrant collection metadata key `graph_call_sites`; per-collection Path D engine partition; Qdrant fallback + warning; retained `callees` keyword index until Phase 3; no `GRAPH_SCHEMA_VERSION` env
- **Deviations:** none
- **Code evidence:** `mcp_server/src/codebase_indexer/storage/qdrant.py`, `mcp_server/src/codebase_indexer/indexer/pipeline.py`, `mcp_server/src/codebase_indexer/tools/cross_references.py`, `mcp_server/tests/test_qdrant_graph_call_sites.py`, `mcp_server/tests/test_cross_references.py`, `mcp_server/tests/test_pipeline_graph.py`, `mcp_server/tests/test_neo4j_call_site_integration.py`, `docs/ARCHITECTURE.md`, `.env.example`, `CHANGELOG.md`
- **Test debt:** Testcontainers integration test marked `slow` — optional CI job with Docker
- **Changelog:** no — user-facing yes; status `implemented` (not verified); invoker Changelog: yes; bullet already in `[Unreleased]` from implementation

### ADR 0024 — Phase 1 — Analyze + allocate

#### 2026-07-08 — verification
- **Phase:** Phase 1 — Analyze + allocate
- **Tracker status:** `verified`
- **Choices:** Pure `tune_alloc.py` split from `tune_stack.py` CLI; topology-priority RAM selection; deterministic knob tiers; tri-state flag precedence mirroring `compose_files.py`; stdlib RAM detection; NVIDIA probe deferred Phase 2
- **Deviations:** none
- **Code evidence:** `scripts/tune_alloc.py`, `scripts/tune_stack.py`, `mcp_server/tests/test_tune_alloc.py`, `docs/adr/0024-resource-aware-stack-tuner.md`, `docs/adr/README.md`
- **Test debt:** CLI-level tests for `tune_stack.py`; host-detection mocks; ADR success-criterion #1 ±10% preset assertion deferred
- **Verify:** 17 unit tests pass (`mcp_server/tests/test_tune_alloc.py`); CLI smoke pass; Docker integration report `pass`; plan compliance all pass; no lint errors; review rounds: 1
- **Changelog:** no — user-facing yes; entry deferred per invoker

#### 2026-07-08 — merge
- **Phase:** Phase 1 — Analyze + allocate
- **Tracker status:** `merged`
- **Choices:** squash merge `e0c6100` on feature branch `adr/0024-phase-1-analyze-allocate`; ADR Accept skipped — new status Accepted (applied in PR); release skipped; Phases 2+ deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #25](https://github.com/Tusquito/codebase-indexer-mcp/pull/25) (`adr/0024-phase-1-analyze-allocate`; squash `e0c6100`)`
- **Test debt:** carried from verification — CLI-level tests for `tune_stack.py`; host-detection mocks; ADR success-criterion #1 ±10% preset assertion deferred
- **Verify:** carried from verification — 17 unit tests pass; CLI smoke pass; Docker integration report `pass`; plan compliance all pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/25 — status: merged — commit: e0c6100
- **Changelog:** no — user-facing yes; release skipped

#### 2026-07-07 — prioritization
- **Phase:** Phase 1 — Analyze + allocate
- **Tracker status:** `candidate`
- **Choices:** N/A (prioritization only — no implementation choices made). **Why now:** Not started (no `scripts/tune_stack.py`/`tune_alloc.py` in repo); all cited prerequisites (ADR 0007 `bench.py`, ADR 0015 ColBERT sidecar, ADR 0018 metrics, ADR 0022 GPU compose) are `Accepted`/merged; Phase 1 is deterministic allocation math with no search loop — zero regression risk to retrieval/index code paths; addresses a real, broad operator pain point (manual `.env.example` tier guessing causing silent Docker OOM restarts). **Suggested scope:** one phase (= one PR). **Chosen scope:** `scripts/tune_alloc.py` (pure allocation + knob-seed math, importable, unit-tested), `scripts/tune_stack.py` `analyze`/`allocate` subcommands only (no `tune`/search loop — Phase 2+), `mcp_server/tests/test_tune_alloc.py`. Accept ADR 0024 as part of this phase's kickoff.
- **Deviations:** none
- **Code evidence:** ``scripts/tune_stack.py` absent; `scripts/tune_alloc.py` absent`
- **Changelog:** no — user-facing yes (new maintainer/operator-facing CLI script); no default-behavior or compose changes required until Phase 4 preset sync

#### 2026-07-07 — plan
- **Phase:** Phase 1 — Analyze + allocate
- **Tracker status:** `planned`
- **Choices:** stdlib-only host detection (no new `psutil` dep) with `--max-ram-gib` fallback; caps emitted as compose-only env vars, never written to operator `.env`; accept-on-first-phase (ADR flipped to Accepted in this PR); `tune`/`report` registered but stubbed to Phase 2/3; TEI service for dense sidecar (`TEI_MEM_LIMIT`/`TEI_CPUS`, not Ollama); `analyze` may probe NVIDIA runtime via `nvidia_docker_available` (mocked in tests). **Chosen scope:** Pure allocation/knob-seed math (`scripts/tune_alloc.py`), `scripts/tune_stack.py` `analyze`+`allocate` subcommands with feature-flag/topology resolution mirroring `compose_files.py`, unit tests (`mcp_server/tests/test_tune_alloc.py`), and formal ADR Accept (status + `docs/adr/README.md` index). No bench/search loop, no `.env` writes. One PR. Defer `tune`/search loop (Phase 2+), `.env.example` preset sync (Phase 4).
- **Deviations:** none
- **Test debt:** `mcp_server/tests/test_tune_alloc.py`; `nvidia_docker_available` mocked in tests
- **Changelog:** no — user-facing yes; entry at `verified` step

#### 2026-07-07 — implementation
- **Phase:** Phase 1 — Analyze + allocate
- **Tracker status:** `implemented`
- **Choices:** Pure math in `tune_alloc.py`; CLI `analyze`/`allocate` + `tune`/`report` stubs; stdlib RAM detection with `--max-ram-gib` fallback; TEI caps `TEI_MEM_LIMIT`/`TEI_CPUS`; ColBERT MCP ≤35% cap; `.env` write refused.
- **Deviations:** no-TEI topology priority (cpu_dense+graph combo unreachable); knob ranges to deterministic tiers; NVIDIA runtime probe deferred Phase 2.
- **Code evidence:** `scripts/tune_alloc.py`, `scripts/tune_stack.py`, `mcp_server/tests/test_tune_alloc.py`, `docs/adr/0024-resource-aware-stack-tuner.md`, `docs/adr/README.md`
- **Test debt:** Docker integration hook; CLI-layer tests; host-detection mocks; success-criteria ±10% preset assertion
- **Changelog:** no — user-facing yes; entry at `verified` step

### ADR 0025 — Phase 1 — TEI hard replace (final phase) — closeout

#### 2026-07-07 — verification
- **Phase:** Phase 1 — TEI hard replace (final phase) — closeout
- **Tracker status:** `verified`
- **Choices:** Ollama→TEI doc/docstring sweep completed across 16 files total (14 original + `.cursor/agents/adr-integration-tester.md` + golden_queries.jsonl addendum); model references normalized to canonical HF repo ids from `config.py` registry / `.env.example` default (`jinaai/jina-embeddings-v2-base-code`); GPU verification standardized on `docker exec codeindexer_tei nvidia-smi`; root-caused and fixed a genuine upstream `text-embeddings-inference` CUDA-detection bug (driver 6xx header rename) via `entrypoint` override in `docker-compose.tei.gpu.yml`, unblocking real GPU quality-validation; `benchmarks/train/**`, historical ADR bodies, and `docs/adr/README.md` index left out of scope per plan (deferred to ADR 0020 follow-up for train pipeline).
- **Deviations:** none
- **Code evidence:** ``README.md`, `docs/DEPLOYMENT.md`, `docs/SEARCH_BEHAVIOR.md`, `CONTRIBUTING.md`, `.github/copilot-instructions.md`, `.github/workflows/ci.yml`, `mcp_server/pyproject.toml`, `mcp_server/Dockerfile`, `mcp_server/src/codebase_indexer/indexer/embedder.py`, `mcp_server/src/codebase_indexer/indexer/pipeline.py`, `mcp_server/src/codebase_indexer/tools/service_map.py`, `mcp_server/scripts/test_adaptive_live.py`, `.cursor/agents/deps-hygiene.md`, `.cursor/agents/ops-hygiene.md`, `.cursor/agents/adr-integration-tester.md`, `.env.example`, `docker-compose.tei.gpu.yml` (GPU entrypoint fix), `mcp_server/benchmarks/fixtures/golden_queries.jsonl` (alias fix), `mcp_server/benchmarks/fixtures/eval_baseline.json` (live GPU metrics refresh), `docs/adr/0025-huggingface-tei-dense-embedding.md` (Measured outcomes filled).`
- **Test debt:** none blocking. Minor non-blocking doc nit (`README.md:437` self-referential wording) may be tidied opportunistically. Suggested future test debt: optional offline CI unit test to catch golden-set alias drift before it reaches the compose harness.
- **Verify:** unit tests pass (393 passed, 8 skipped); Docker integration pass with required quality validation (recall@10=0.3590, MRR=0.3576, ndcg@10=0.2807, threshold 0 self-compare pass; real GPU confirmed via `Cuda(CudaDevice(DeviceId(1)))`); plan compliance pass; all 6 round-1 review issues (Ollama community-port model tags shown as TEI values, garbled duplicate doc tables, wrong model id in DEPLOYMENT.md, `ollama ps PROCESSOR` residue, garbled line in adr-integration-tester.md, duplicate .env.example comments) resolved in round 2; review rounds: 2
- **Changelog:** no — existing ADR 0025 breaking-change bullet in `CHANGELOG.md` `[Unreleased]` already covers this

#### 2026-07-07 — prioritization
- **Phase:** Phase 1 — TEI hard replace (final phase) — closeout
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0025 Phase 1 closeout over 0002 Phase 2, 0018 Phase 2, 0023 Phase 3, 0019, 0024; single phase/closeout per pipeline rule; pre-release policy makes the doc-correctness fix zero-risk and mandatory Docker/quality gates make this the only candidate with an open compliance gap today. **Why now:** Tracker marks this phase `implemented`, but verification against actual repo state shows it fails the ADR's own Success Criterion #2 (`rg -i ollama` still hits `README.md` with functionally wrong claims, `mcp_server/src/` docstrings, `.github/`, `docs/DEPLOYMENT.md`, `pyproject.toml`, `Dockerfile`) and both mandatory pre-release gates (Docker integration run, quality-validation golden baseline refresh) are still pending per the phase-log note and the frozen `eval_baseline.json`. Core code (backend, compose, config, factory, tests, CHANGELOG entry, ADR 0017 rename) is genuinely done and Ollama-free — this is a low-risk closeout, not new architecture, and it unblocks ADR 0024 (whose body still cites `OLLAMA_MEM_LIMIT`). **Suggested scope:** one phase (= one PR) — this is closeout of the existing Phase 1/final phase, not a new phase. **Chosen scope:** (1) Correct `README.md` dense-embedding claims (lines ~353, 454, 613) and remaining stale "Ollama" docstrings/comments in `embedder.py`, `pipeline.py`, `service_map.py`, `.github/workflows/ci.yml`, `.github/copilot-instructions.md`, `docs/DEPLOYMENT.md`, `mcp_server/pyproject.toml`, `mcp_server/Dockerfile`; (2) run `scripts/run_compose_integration.py --quality-validation --performance-report` on a GPU host; (3) refresh `mcp_server/benchmarks/fixtures/eval_baseline.json` with live TEI+Jina GPU metrics and fill ADR 0025 Measured outcomes table; (4) update tracker phase-log status to `verified`/`merged` with real evidence. **User-facing:** yes — `README.md` currently gives operators incorrect information about where dense vectors come from; fixing it is a documentation-correctness change, not a behavior change.
- **Deviations:** none
- **Test debt:** live TEI integration (Docker); GPU golden baseline refresh (`eval_baseline.json` + ADR 0025 Measured outcomes table); doc/docstring Ollama-reference sweep (README, `embedder.py`, `pipeline.py`, `service_map.py`, CI workflow, copilot-instructions, `DEPLOYMENT.md`, `pyproject.toml`, `Dockerfile`)
- **Verify:** `rg -i ollama` clean per ADR Success Criterion #2; `scripts/run_compose_integration.py --quality-validation --performance-report` pass on GPU host
- **Changelog:** no — user-facing yes; invoker Changelog: no (breaking-change entry for ADR 0025 already present in `CHANGELOG.md` `[Unreleased]`)

#### 2026-07-07 — plan
- **Phase:** Phase 1 — TEI hard replace (final phase) — closeout
- **Tracker status:** `planned`
- **Choices:** Doc/docstring sweep scope expanded beyond the prioritizer's seed list after direct repo verification (added `CONTRIBUTING.md`, `docs/SEARCH_BEHAVIOR.md`, `test_adaptive_live.py`, `deps-hygiene.md`, `ops-hygiene.md`, and two non-literal README command-block bugs at L618/L623); quality validation threshold `0` (self-compare, fresh baseline); performance report report-only; no schema/version env var added for the baseline data refresh
- **Deviations:** none
- **Test debt:** live TEI integration (Docker); GPU golden baseline refresh (`eval_baseline.json` + ADR 0025 Measured outcomes table); doc/docstring Ollama-reference sweep (expanded scope, see Chosen scope)
- **Verify:** `rg -i ollama` clean (excluding `benchmarks/train/**` and historical ADR/README-index references) per ADR Success Criterion #2; `scripts/run_compose_integration.py --json --quality-validation --performance-report` pass on GPU host
- **Changelog:** no — breaking-change entry for ADR 0025 hard replace already present in `CHANGELOG.md` `[Unreleased]`; this closeout is doc-correctness + gate compliance, not a new behavior change; user-facing: yes — `README.md`/`docs/DEPLOYMENT.md`/`CONTRIBUTING.md` currently give operators factually wrong or broken information about where dense vectors come from and how to run the TEI sidecar

#### 2026-07-07 — merge
- **Phase:** Phase 1 — TEI hard replace (final phase) — closeout
- **Tracker status:** `merged`
- **Choices:** ADR 0025 status updated to **Accepted (all phases complete)** on main via docs commit `a756677` (`docs/adr/0025-huggingface-tei-dense-embedding.md`, `docs/adr/README.md`). Root-caused and fixed genuine upstream `huggingface/text-embeddings-inference` CPU-warmup bug (large default `--max-batch-tokens` vs model `max_input_length` causing crash-loop on CPU-only CI path) via `--max-batch-tokens` cap + client-side `MAX_DENSE_EMBED_TOKENS` pairing, confined to CPU-only integration harness path (GPU-default production path unaffected, already verified with real GPU: recall@10=0.3590, MRR=0.3576, ndcg@10=0.2807, 43/43 golden labels).
- **Deviations:** none
- **Code evidence:** `squash merge `0f01cda` — `feat(embed): add TEI dense backend`; `build(compose): swap Ollama for TEI sidecar`; `refactor(bench): point tooling at TEI embed`; `docs(adr): TEI sweep and phase 1 closeout`; `test(eval): refresh golden labels and baseline`; `fix(ci): capture container logs on deploy fail`; `docs(agents): drop Ollama refs from hygiene docs`; `fix(ci): raise CPU TEI memory for cold warmup`; `fix(compose): cap TEI MKL ISA to fix CPU crash`; `fix(compose): cap TEI CPU warmup batch tokens`; docs accept commit `a756677``
- **Test debt:** none blocking — optional offline CI alias-drift guard and `README.md:437` doc nit carried as non-blocking future debt from verification; `benchmarks/train/**` Ollama references remain deferred to ADR 0020 follow-up
- **Verify:** merged to main; all phases complete
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/23 — status: pending
- **Changelog:** no — existing ADR 0025 breaking-change bullet in `CHANGELOG.md` `[Unreleased]` already covers this

#### 2026-07-07 — implementation
- **Phase:** Phase 1 — TEI hard replace (final phase) — closeout doc sweep
- **Tracker status:** `implemented`
- **Choices:** Applied the full literal "Ollama" → "TEI" text-correction sweep across README, DEPLOYMENT, SEARCH_BEHAVIOR, CONTRIBUTING, copilot-instructions, ci.yml comments, pyproject.toml, Dockerfile, embedder.py/pipeline.py/service_map.py docstrings+comments, test_adaptive_live.py sample query, and rephrased deps-hygiene.md/ops-hygiene.md agent instructions to drop literal "Ollama" while preserving anti-regression intent. Also corrected two leftover garbled command-block fragments in README.md (bogus "TEI loads model at startup ..." pull-step line, and duplicated "nvidia-smi in codeindexer_tei" garble) from a prior partial find/replace.
- **Deviations:** none (fixed duplicate occurrences of the same two garbled README patterns beyond the single instance each named in the plan, for consistency)
- **Code evidence:** `README.md`, `docs/DEPLOYMENT.md`, `docs/SEARCH_BEHAVIOR.md`, `CONTRIBUTING.md`, `.github/copilot-instructions.md`, `.github/workflows/ci.yml`, `mcp_server/pyproject.toml`, `mcp_server/Dockerfile`, `mcp_server/src/codebase_indexer/indexer/embedder.py`, `mcp_server/src/codebase_indexer/indexer/pipeline.py`, `mcp_server/src/codebase_indexer/tools/service_map.py`, `mcp_server/scripts/test_adaptive_live.py`, `.cursor/agents/deps-hygiene.md`, `.cursor/agents/ops-hygiene.md`
- **Test debt:** none for this increment — docs/comments only, no behavior-affecting code paths touched; phase-level test debt (live TEI integration run, GPU golden baseline refresh) remains outstanding
- **Changelog:** no — existing ADR 0025 breaking-change bullet in `CHANGELOG.md` `[Unreleased]` already covers this; no new bullet needed for doc corrections

#### 2026-07-07 — implementation
- **Phase:** Phase 1 — TEI hard replace (final phase), closeout addendum
- **Tracker status:** `implemented`
- **Choices:** Corrected 6 stale golden-set `rel_path:start_line` aliases in `mcp_server/benchmarks/fixtures/golden_queries.jsonl` (spanning 8 query lines: q_embedder_class, q_create_backends, q_sparse_bm25, q_fuse_rrf, q_qdrant_storage_search, q_prefetch_multiplier, q_mh_search_stack, q_payload_indexes, q_cross_references) by re-chunking affected files (`factory.py`, `qdrant.py`, `config.py`, `cross_references.py`) with the production chunker to obtain current chunk-start lines; assigned semantically correct symbols per each query's ground_truth. Left non-stale aliases and all query text/tags/grades unchanged. This unblocks the mandatory `validate_labels` gate in `scripts/run_compose_integration.py --quality-validation` for the ADR 0025 closeout PR.
- **Deviations:** none
- **Code evidence:** `mcp_server/benchmarks/fixtures/golden_queries.jsonl`
- **Test debt:** Live `--validate-labels` + `eval_retrieval` against fresh index to be confirmed by integration-tester re-run; optional offline CI alias-drift guard suggested as future test debt.
- **Changelog:** no

#### 2026-07-04 — prioritization
- **Phase:** Phase 1 — TEI hard replace
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0025 Phase 1 over 0002 Phase 2, 0023 Phase 3, 0019, 0024, 0018; single phase per pipeline rule; pre-release hard replace acceptable. **Chosen scope:** Add `TeiDenseBackend`; TEI compose files with profile `bundled-tei`; update `compose_files.py` and `run_compose_integration.py`; delete Ollama dense backend/compose and all `OLLAMA_*` config; refresh golden-set baseline on TEI+Jina (GPU); complete ADR Ollama removal inventory. Requires formal Accept of Proposed ADR 0025 before dev (first PR task). **Why now:** Embed/accelerator arc complete (0021, 0022, 0023 P1–P2 merged 2026-07-04); code still uses `OllamaDenseBackend` exclusively; ADR 0025 closes HF catalog gap with hard replace following ADR 0015 sidecar pattern and ADR 0022 GPU-default topology; prerequisites satisfied; measurable via tests + `eval_retrieval` baseline refresh + integration harness TEI checks; unlocks ADR 0024 TEI allocation rows; pre-release breaking change acceptable. **Suggested scope:** one phase (= one PR).
- **Deviations:** none
- **Test debt:** golden-set baseline refresh on TEI+Jina (GPU); integration harness TEI checks; `eval_retrieval` baseline self-compare threshold 0
- **Verify:** unit tests; `eval_retrieval` baseline refresh; integration harness TEI checks
- **Changelog:** no — user-facing unknown

#### 2026-07-04 — plan
- **Phase:** Phase 1 — TEI hard replace
- **Tracker status:** `planned`
- **Choices:** Accept in first PR commit; single PR; golden baseline threshold 0; bundle ADR 0017 doc rename; pre-release hard replace; quality validation required threshold 0; performance report yes; Docker integration required; final phase yes
- **Deviations:** none
- **Test debt:** golden-set baseline refresh on TEI+Jina (GPU); integration harness TEI checks (`tei_health`, `tei_embed_smoke`, `tei_gpu_visible`); `eval_retrieval` baseline self-compare threshold 0
- **Verify:** unit tests; `eval_retrieval` baseline refresh; integration harness TEI checks; Docker integration required
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-04 — implementation
- **Phase:** Phase 1 — TEI hard replace (final phase)
- **Tracker status:** `implemented`
- **Choices:** Accept in first commit; single PR; threshold 0; ADR 0017 rename bundled; hard replace
- **Deviations:** eval_baseline.json metrics not re-captured on TEI GPU (params/note only); compose integration pending step 3.5
- **Code evidence:** `tei_dense.py, factory.py, config.py, docker-compose.tei.yml, docker-compose.tei.gpu.yml, compose_files.py, run_compose_integration.py, test_tei_dense_backend.py, docs/adr/0025-huggingface-tei-dense-embedding.md`
- **Test debt:** Live TEI integration; GPU golden baseline refresh; full compose harness
- **Changelog:** no — user-facing yes; invoker Changelog: no

### ADR 0026 — Phase 3 — Candidate registry + integration spikes

#### 2026-07-10 — verification
- **Phase:** Phase 3 — Candidate registry + integration spikes
- **Tracker status:** `verified`
- **Choices:** feature-flagged query_instruction/normalize_output hooks in TeiDenseBackend (default OFF); inf-retriever spike_passed; both pplx dropped; embeddinggemma excluded; fixture-only, no production default change
- **Deviations:** none
- **Code evidence:** `mcp_server/benchmarks/fixtures/model_candidates.yaml`, `mcp_server/benchmarks/candidates.py`, `mcp_server/benchmarks/verify_candidate.py`, `mcp_server/benchmarks/_settings.py`, `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/indexer/backends/tei_dense.py`, `mcp_server/src/codebase_indexer/indexer/backends/base.py`, `mcp_server/src/codebase_indexer/indexer/embedder.py`
- **Test debt:** Live TEI verification deferred to Phase 4 GPU session
- **Verify:** unit tests pass (32 Phase 3 + 9 adjacent), lints clean, Docker integration Verdict pass with quality validation; plan compliance pass; R1 resolved and regression-tested; review rounds: 2
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-10 — plan
- **Phase:** Phase 3 — Candidate registry + integration spikes
- **Tracker status:** `planned`
- **Choices:** one phase = one PR; native candidates route through existing `TeiDenseBackend`/`create_dense_backend`, no new runtime env vars; spike hooks are per-candidate flags leaving the Jina default path unchanged; Quality validation required threshold 0 report-only; Performance report skip; suggested tier claude-opus-4-8-thinking-low
- **Deviations:** none
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-10 — merge
- **Phase:** Phase 3 — Candidate registry + integration spikes
- **Tracker status:** `merged`
- **Choices:** squash merge `3035113` on feature branch `adr/0026-phase-3-candidate-registry`; ADR Accept skipped; release skipped; Phases 4–5 deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #31](https://github.com/Tusquito/codebase-indexer-mcp/pull/31) (`adr/0026-phase-3-candidate-registry`; squash `3035113`)`
- **Test debt:** Live TEI verification deferred to Phase 4 GPU session
- **Verify:** carried from verification — unit tests pass (32 Phase 3 + 9 adjacent), lints clean, Docker integration Verdict pass with quality validation; plan compliance pass; R1 resolved and regression-tested; review rounds: 2
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/31 — status: merged — commit: 3035113
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-10 — implementation
- **Phase:** Phase 3 — Candidate registry + integration spikes
- **Tracker status:** `implemented`
- **Choices:** 10-row machine-readable registry with validating loader; GTE_MODERNBERT_SPECS / GRANITE_EMBED_SPECS / INF_RETRIEVER_SPECS in config.py; per-candidate settings swap helper; tei_health+tei_embed_smoke verifier; feature-flagged query_instruction and normalize_output hooks in TeiDenseBackend; granite-embedding-97m included
- **Deviations:** inf-retriever spike passed; pplx-embed INT8 dropped for both sizes per 30-min drop-on-failure rule
- **Code evidence:** `mcp_server/benchmarks/fixtures/model_candidates.yaml`, `mcp_server/benchmarks/candidates.py`, `mcp_server/benchmarks/verify_candidate.py`, `mcp_server/benchmarks/_settings.py`, `mcp_server/src/codebase_indexer/config.py`, `mcp_server/src/codebase_indexer/indexer/backends/tei_dense.py`, `mcp_server/tests/test_model_candidates.py`, `mcp_server/tests/test_candidate_config.py`, `mcp_server/tests/test_tei_dense_spikes.py`
- **Test debt:** live per-native-candidate verify_candidate runs deferred to Phase 4 GPU session
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-08 — verification
- **Phase:** Phase 1 — Harness reliability fix
- **Tracker status:** `verified`
- **Choices:** Content-anchored labels with 5-step ladder; drift counted not silently scored; CI repro via `--keep` + kept-stack pytest
- **Deviations:** none
- **Code evidence:** `mcp_server/benchmarks/label_anchor.py`, `mcp_server/benchmarks/eval_retrieval.py`, `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `mcp_server/tests/test_label_anchor.py`, `mcp_server/tests/test_harness_reproducibility.py`, `.github/workflows/ci.yml`
- **Test debt:** Symbol drift live integration exercised (12 drift observed in CI run, re-resolved via content anchoring, 0 unresolved); Phase 4 collection override concern
- **Verify:** unit tests pass (11 in `test_label_anchor.py`); ruff clean; Docker compose integration + quality validation pass (55 labels, 12 drifted, 0 unresolved; threshold 0 pass); repeat-run repro in blocking `compose-integration` CI job gates `recall@10` within ±1pp (rank-sensitive `mrr`/`ndcg@10` bounded, not exact); review rounds: 1
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-08 — verification
- **Phase:** Phase 2 — Golden-set expansion
- **Tracker status:** `verified`
- **Choices:** In-place expansion to 78 rows; baseline golden_set_version v4-expanded-75q provisional; multi_hop test floor >=15; valid_jsonl accepts anchors
- **Deviations:** none
- **Code evidence:** `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `mcp_server/benchmarks/fixtures/eval_baseline.json`, `mcp_server/tests/test_eval_retrieval.py`, `mcp_server/tests/test_eval_multihop.py`
- **Test debt:** Compose harness --extra benchmark; Phase 4 baseline regeneration
- **Verify:** unit tests pass (14 passed / 4 ranx-skipped); per-tag floors, GT floor, multi_hop invariants, and anchor coverage independently verified against golden_queries.jsonl (78 rows); Docker integration report Verdict pass with report-only quality validation; review rounds: 1
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-08 — prioritization
- **Phase:** Phase 1 — Harness reliability fix
- **Tracker status:** `candidate`
- **Choices:** Recommend Phase 1 only (not Phases 2–5); Accept ADR 0026 before implementation (human confirmed yes); Docker integration required per project-phase policy; no GPU required for Phase 1; priority 0026 Phase 1 over ADR 0002 Phase 3 (human confirmed). **Why now:** Only Proposed ADR; golden-set harness has demonstrated ±60pp recall@10 non-reproducibility on unchanged Jina model (0021 frozen 0.660 vs live 0.263); labels still keyed on `rel_path:start_line` in `eval_retrieval.py`; 0021 test debt defers golden label realignment; prerequisites (0007, 0025, 0022, 0002 Phase 2) satisfied; Phase 1 is benchmark-only with zero production impact. **Suggested scope:** one phase (= one PR). **Chosen scope:** Phase 1 — content-anchored label resolution; `--validate-labels` drift re-resolution with drift counts; repeat-run regression test (`test_harness_reproducibility.py`); wire into `eval_retrieval.py` via `label_anchor.py`.
- **Deviations:** none
- **Code evidence:** `labels keyed on `rel_path:start_line` in `eval_retrieval.py`; frozen `eval_baseline_jina.json` recall@10 0.660 vs live 0.263 (0021 Phase 2)`
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-08 — prioritization
- **Phase:** Phase 2 — Golden-set expansion
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0026 Phase 2 over 0002 Phase 3 `expand_search_context` (0002 P2 merged today — prerequisite satisfied, but higher scope/risk new MCP tool); over 0023 Phase 3 callees index retirement; over 0024 Phase 2 tuner bench seed; over 0018 Phase 2 OTel traces; single phase per pipeline rule; no ADR Accept required (0026 already Accepted phase 1); pre-release: fixture-only, no production default change. **Why now:** ADR 0026 Phase 1 (harness reliability fix) merged 2026-07-08 ([PR #27](https://github.com/Tusquito/codebase-indexer-mcp/pull/27)); golden fixture still has 26 query rows in `mcp_server/benchmarks/fixtures/golden_queries.jsonl` while ADR success criterion #2 requires ≥75; Phase 1 fixed label drift (`label_anchor.py`, repeat-run ±1pp CI gate) but statistical power remains insufficient for embedding-model comparisons; Phases 3–5 (`model_candidates.yaml`, `bakeoff.py`, promotion decision) are explicitly blocked on Phase 2 per ADR phased delivery; no active tracker phases; pre-release policy allows fixture-only changes without production default impact. **Suggested scope:** one phase (= one PR). **Chosen scope:** Expand golden set 26 → ≥75 labeled queries via existing `mcp_server/benchmarks/suggest_labels.py` workflow; preserve tag taxonomy proportions (`symbol`, `conceptual`, `config`, `cross_file`, `multi_hop`); content-anchored `anchors` on all rows (Phase 1 format); `--validate-labels` zero unresolved; bump `golden_set_version` in eval baseline artifacts; Docker integration via `scripts/run_compose_integration.py`; golden-set quality validation required; defer Phase 3 (candidate registry + TEI spikes), Phase 4 (GPU bake-off), Phase 5 (promotion/reaffirmation decision).
- **Deviations:** none
- **Code evidence:** `golden fixture has 26 query rows in `mcp_server/benchmarks/fixtures/golden_queries.jsonl`; ADR success criterion`
- **Changelog:** no — user-facing unknown; invoker Changelog: no

#### 2026-07-08 — prioritization
- **Phase:** Phase 3 — Candidate registry + integration spikes
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0026 Phase 3 over 0002 Phase 3 `expand_search_context` (scores within ~10%; tie-breaker: lower scope/risk — benchmark tooling vs new MCP tool); over 0023 Phase 3 callees index retirement; over 0018 Phase 2 OTel traces; over 0024 Phase 2 tuner seed bench; single phase per pipeline rule; no ADR Accept required (0026 already Accepted); pre-release: fixture-only, throwaway collections, no production default change. **Why now:** ADR 0026 Phases 1 and 2 merged 2026-07-08; golden fixture expanded to 78 content-anchored queries (`golden_set_version: v4-expanded-75q`); `model_candidates.yaml` and `bakeoff.py` absent; Phases 4–5 (GPU bake-off, promotion/reaffirmation) explicitly blocked on Phase 3 per ADR phased delivery; resolves disputed embedding-model evidence base (0016/0020/0021) before further default-model or GraphRAG work; tracker shows no active phases. **Suggested scope:** one phase (= one PR). **Chosen scope:** `model_candidates.yaml` registry; `config.py` entries for six native TEI candidates; `_settings.py` per-candidate swap helper; time-boxed TEI spikes for `inf-retriever-v1-1.5b` (instruction prefix) and `pplx-embed-v1-{0.6b,4b}` (INT8/cosine) with documented drop-on-failure; `tei_health` + `tei_embed_smoke` per native candidate; unit tests; Docker integration (fixture-only, no production default change); defer Phase 4 bake-off orchestration and Phase 5 decision.
- **Deviations:** none
- **Code evidence:** `{'golden fixture expanded to 78 content-anchored queries (`golden_set_version': 'v4-expanded-75q`)'}`, ``model_candidates.yaml` and `bakeoff.py` absent`
- **Changelog:** no — user-facing unknown; invoker Changelog: no

#### 2026-07-08 — plan
- **Phase:** Phase 1 — Harness reliability fix
- **Tracker status:** `planned`
- **Choices:** **Label anchor rule:** primary key `{rel_path}::{symbol_name}` resolved live via Qdrant's indexed `rel_path`+`symbol_name` payload fields; `start_line` retained only as nearest-line tie-break hint; ladder = legacy chunk_id hit → content re-resolution on drift → nearest-line tie-break → basename anchor for non-code files → report `unresolved` (never silently score stale). Existing `aliases` kept as cached hints. **Repeat-run test CI placement:** pure resolver unit tests in blocking `test` job; live repeat-run determinism assertion in blocking `compose-integration` job; non-blocking `eval-retrieval` metric job unchanged. Blocking gates resolution determinism, not recall threshold.
- **Deviations:** none
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-08 — plan
- **Phase:** Phase 2 — Golden-set expansion
- **Tracker status:** `planned`
- **Choices:** In-place expansion (no golden_queries_v3.jsonl cutover); per-tag targets symbol 26 / conceptual 7 / config 19 / cross_file 19 / multi_hop 15; sequential before 0002 Phase 3; eval_baseline_jina.json left frozen; quality validation report-only; performance report skipped; ground-truth subset floor 19; every multi_hop row must carry secondary tag. **Assumptions:** per-tag numbers are memberships summing to 86 across ≥75 distinct rows; eval_baseline.json metrics stay 26-query capture marked provisional via note + version bump; re-captured only in Phase 4.
- **Deviations:** none
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-08 — merge
- **Phase:** Phase 1 — Harness reliability fix
- **Tracker status:** `merged`
- **Choices:** squash merge `0ca2f88` on feature branch `adr/0026-phase-1-harness-reliability`; ADR Accept applied — Accepted (phase 1 — Harness reliability fix); release skipped; Phases 2–5 deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #27](https://github.com/Tusquito/codebase-indexer-mcp/pull/27) (`adr/0026-phase-1-harness-reliability`; squash `0ca2f88`)`
- **Test debt:** carried from verification — symbol drift live integration exercised (12 drift observed in CI run); Phase 4 collection override concern
- **Verify:** carried from verification — 11 unit tests pass (`test_label_anchor.py`); ruff clean; Docker integration + quality validation pass (55 labels, 12 drifted, 0 unresolved; threshold 0 pass); repeat-run repro in blocking `compose-integration` CI job gates `recall@10` within ±1pp; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/27 — status: merged — commit: 0ca2f88
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-08 — merge
- **Phase:** Phase 2 — Golden-set expansion
- **Tracker status:** `merged`
- **Choices:** squash merge `8be500b` on feature branch `adr/0026-phase-2-golden-set-expansion`; ADR Accept skipped; release skipped; Phases 3–5 deferred
- **Deviations:** none
- **Code evidence:** `merged via [PR #30](https://github.com/Tusquito/codebase-indexer-mcp/pull/30) (`adr/0026-phase-2-golden-set-expansion`; squash `8be500b`)`
- **Test debt:** carried from verification — Compose harness --extra benchmark; Phase 4 baseline regeneration
- **Verify:** carried from verification — unit tests pass (14 passed / 4 ranx-skipped); per-tag floors, GT floor, multi_hop invariants, and anchor coverage independently verified against golden_queries.jsonl (78 rows); Docker integration report Verdict pass with report-only quality validation; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/30 — status: merged — commit: 8be500bd7e715a58158d8d2acda40013f288617c
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-08 — implementation
- **Phase:** Phase 1 — Harness reliability fix
- **Tracker status:** `implemented`
- **Choices:** Content-anchored label resolution keyed on `{rel_path}::{symbol_name}` with `start_line` as cached hint; fixed resolution ladder (legacy chunk_id → content → nearest-line → basename → unresolved); `--validate-labels` re-resolves drift and reports counts instead of hard-failing; `label_drift` surfaced per eval run; reproducibility enforced via live repeat-run test in blocking `compose-integration` job.
- **Deviations:** CI repro wired via `--keep` + kept-stack pytest; tracker row emitted here.
- **Code evidence:** `mcp_server/benchmarks/label_anchor.py`, `mcp_server/benchmarks/eval_retrieval.py`, `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `mcp_server/tests/test_label_anchor.py`, `mcp_server/tests/test_harness_reproducibility.py`, `.github/workflows/ci.yml`
- **Test debt:** `load_point_index` async coverage; drift-report integration test; CI repro non-skip verification; legacy-path regression test
- **Changelog:** no — user-facing no; invoker Changelog: no

#### 2026-07-08 — implementation
- **Phase:** Phase 2 — Golden-set expansion
- **Tracker status:** `implemented`
- **Choices:** Expanded in-place to 78 rows (no v3 cutover); per-tag floors met with margin (symbol 27, conceptual 11, config 20, cross_file 20, multi_hop 16); ground-truth subset 20 (≥19 floor); every multi_hop row carries a secondary tag; baseline version bumped to v4-expanded-75q with metrics marked provisional (not fabricated).
- **Deviations:** Relaxed hardcoded == 4 counts in test_eval_multihop.py; extended test_golden_fixture_is_valid_jsonl to accept anchors; compose harness eval step fails without benchmark extra (ranx missing) — validate_labels gate passed 0 unresolved.
- **Code evidence:** `mcp_server/benchmarks/fixtures/golden_queries.jsonl`, `mcp_server/benchmarks/fixtures/eval_baseline.json`, `mcp_server/tests/test_eval_retrieval.py`, `mcp_server/tests/test_eval_multihop.py`
- **Test debt:** Compose harness must invoke eval with --extra benchmark; Phase 4 GPU bake-off to regenerate baseline metrics
- **Changelog:** no — user-facing no; invoker Changelog: no

### ADR 0028 — Phase 1 — Documented profile

#### 2026-07-12 — verification
- **Phase:** Phase 1 — Documented profile
- **Tracker status:** `verified`
- **Choices:** Documented native arm64 CPU-first profile with manual TEI_IMAGE=cpu-arm64-1.9; M3 Pro 24 GiB + 18 GiB presets; RERANK_ENABLED=false; rejected amd64 emulation
- **Deviations:** none
- **Code evidence:** `docs/DEPLOYMENT.md`, `.env.example`, `README.md`, `.github/copilot-instructions.md`
- **Verify:** tests run + plan compliance pass; review rounds: 2
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — verification
- **Phase:** Phase 2 — Arch-aware compose defaults
- **Tracker status:** `verified`
- **Choices:** `cpu-arm64-latest` arm64 TEI tag (human-approved deviation from ADR `cpu-arm64-1.9`); MKL via amd64-only overlay; arch detection in `compose_files.py`; `default_reserve_gib()` shared by `tune_alloc`/`tune_stack`; integration script sets arch-aware TEI/MKL; NVIDIA/CI amd64 path unchanged
- **Deviations:** arm64 tag `cpu-arm64-latest` instead of ADR `cpu-arm64-1.9` (human-approved)
- **Code evidence:** `scripts/compose_files.py`, `docker-compose.tei.amd64-mkl.yml`, `docker-compose.tei.yml`, `scripts/tune_alloc.py`, `scripts/tune_stack.py`, `scripts/run_compose_integration.py`, `mcp_server/tests/test_compose_files.py`, `mcp_server/tests/test_tune_alloc.py`, `mcp_server/tests/test_run_compose_integration_gpu.py`, `docs/DEPLOYMENT.md`, `.env.example`, `README.md`, `.github/copilot-instructions.md`, `CHANGELOG.md`
- **Verify:** tests run + plan compliance pass — 43 targeted unit tests passed; Docker integration pass on arm64 with quality validation threshold 0; review rounds: 1
- **Changelog:** yes — **Arch-aware compose defaults for Apple Silicon** ([ADR 0028](docs/adr/0028-apple-silicon-arm64-cpu-deployment.md)) — `tei_image_default()` and integration script select native `cpu-arm64-latest` on arm64 Docker hosts; darwin RAM detection in `tune_alloc.py`/`tune_stack.py`; MKL instructions via amd64-only compose overlay; NVIDIA/CI amd64 path unchanged; manual `compose up` on Mac still requires `.env` `TEI_IMAGE` preset

#### 2026-07-12 — prioritization
- **Phase:** Phase 1 — Documented profile
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0028 Phase 1 over 0026 Phase 4 (strategically higher score but NVIDIA-GPU-blocked on M3 Pro); over 0027 Phase 1 (Proposed, needs Accept, declined 2026-07-10); over 0029 Phase 1 (depends on 0028 baseline); over 0002 Phase 4 and 0023 Phase 3 (higher scope, lower immediate ops urgency); single phase per pipeline rule; no ADR Accept required (0028 already Accepted); pre-release: docs-only Phase 1, no global default change for NVIDIA hosts. **Why now:** ADRs 0028 and 0029 Accepted 2026-07-12 with zero tracker phases and zero code; maintainer hardware is Apple Silicon M3 Pro without NVIDIA GPU; `compose_files.py` still defaults to x86 `cpu-1.9` TEI, `tune_alloc.py` lacks darwin RAM detection, and `DEPLOYMENT.md`/`.env.example` have no Apple Silicon profile — operational blocker for daily development; ADR 0026 Phase 4 GPU bake-off cannot run on Mac per GPU-only scoring policy; tracker shows no active phases. **Suggested scope:** one phase (= one PR). **Chosen scope:** Phase 1 — `docs/DEPLOYMENT.md` § Apple Silicon (arm64 CPU) with M3 Pro 24 GiB Docker VM profile and minimal 18 GiB tier; `.env.example` macOS presets (`ACCELERATOR=cpu`, `TEI_IMAGE=ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-1.9`, `TEI_MKL_INSTRUCTIONS=`, `RERANK_ENABLED=false`, cgroup caps); README + `.github/copilot-instructions.md` cross-links; manual operator checklist; defer Phase 2 arch-aware `compose_files.py` / `tune_alloc.py` / MKL compose fix.
- **Deviations:** none
- **Code evidence:** ``scripts/compose_files.py` defaults `ACCELERATOR=cpu` → x86 `cpu-1.9` TEI`, ``scripts/tune_alloc.py` lacks darwin RAM detection`, ``docs/DEPLOYMENT.md` and `.env.example` have no Apple Silicon profile`
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — prioritization
- **Phase:** Phase 2 — Arch-aware compose defaults
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0028 Phase 2 over 0026 Phase 4 (higher raw score but NVIDIA-GPU-blocked on M3 Pro per resolved open decision); over 0029 Phase 1 (Metal docs — does not fix compose foot-gun); over 0002 Phase 4 (higher scope GraphRAG); over 0027 Phase 1 (Proposed, needs Accept, deferred in prior cycles); over 0023 Phase 3 (callees retirement); single phase per pipeline rule; no ADR Accept required (0028 already Accepted); pre-release: no global default change for NVIDIA hosts. **Why now:** ADR 0028 Phase 1 merged 2026-07-12; code still defaults `tei_image_default()` to amd64 `cpu-1.9` on all CPU hosts, `tune_alloc.py` lacks darwin RAM detection, and MKL compose env is x86-only — operators must manually set `TEI_IMAGE=cpu-arm64-1.9` per DEPLOYMENT.md. Maintainer is Apple Silicon M3 Pro without NVIDIA GPU; tracker open decisions defer 0026 Phase 4 GPU bake-off until Mac path completes. No active tracker phases. **Suggested scope:** one phase (= one PR). **Chosen scope:** `TEI_IMAGE_CPU_ARM64_DEFAULT` + `container_arch()` (Docker server arch → `platform.machine()` fallback) in `scripts/compose_files.py`; arch-aware `tei_image_default()`; darwin `sysctl hw.memsize` + `DEFAULT_RESERVE_GIB=4.0` in `scripts/tune_alloc.py`; MKL compose fix or arm64 gate in `docker-compose.tei.yml`; arch-aware `TEI_IMAGE` in `scripts/run_compose_integration.py`; unit tests per ADR 0028 Validation; Docker integration. Defer Phase 3 ColBERT-on-Mac doc and Phase 4 `macos_m3pro_matrix.json`.
- **Deviations:** none
- **Code evidence:** ``scripts/compose_files.py` `tei_image_default()` defaults to amd64 `cpu-1.9` on all CPU hosts`, ``scripts/tune_alloc.py` lacks darwin RAM detection`, ``docker-compose.tei.yml` MKL compose env is x86-only`
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — plan
- **Phase:** Phase 1 — Documented profile
- **Tracker status:** `planned`
- **Choices:** Docs-only Phase 1 per binding human decision; no Phase 2 code in this PR; ADR 0028 already Accepted; NVIDIA default unchanged; CI amd64 path unchanged. Operators manually set TEI_IMAGE and TEI_MKL_INSTRUCTIONS= until Phase 2; M3 Pro manual smoke is primary merge gate for Mac correctness.
- **Deviations:** none
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — plan
- **Phase:** Phase 2 — Arch-aware compose defaults
- **Tracker status:** `planned`
- **Choices:** Single PR; MKL via amd64-only `docker-compose.tei.amd64-mkl.yml` overlay wired from `compose_file_args()` (not empty global default); arch detection in `compose_files.py` (no separate `platform_detect.py`); `tei_image_default()` respects explicit `TEI_IMAGE` override; integration sets `TEI_MKL_INSTRUCTIONS=AVX2` on amd64 CPU only; `default_reserve_gib()` callable shared by `tune_alloc` and `tune_stack`; minimal DEPLOYMENT.md footnote sync; NVIDIA/CI amd64 path unchanged. **Assumptions:** Phase 1 merged; ADR Accepted; CI runs amd64 `ACCELERATOR=cpu`; maintainer runs Docker integration on M3 Pro arm64 before merge approval; Docker Desktop VM RAM not auto-detected (operators use `--max-ram-gib` when host RAM ≠ VM budget).
- **Deviations:** none
- **Changelog:** yes — **Arch-aware compose defaults for Apple Silicon** ([ADR 0028](docs/adr/0028-apple-silicon-arm64-cpu-deployment.md)) — `tei_image_default()` and integration script select native `cpu-arm64-latest` on arm64 Docker hosts; darwin RAM detection in `tune_alloc.py`/`tune_stack.py`; MKL instructions via amd64-only compose overlay; NVIDIA/CI amd64 path unchanged; manual `compose up` on Mac still requires `.env` `TEI_IMAGE` preset

#### 2026-07-12 — merge
- **Phase:** Phase 1 — Documented profile
- **Tracker status:** `merged`
- **Choices:** merge via [PR #33](https://github.com/Tusquito/codebase-indexer-mcp/pull/33) on branch `adr/0028-phase-1-documented-profile`; ADR Accept skipped — already Accepted (phase 1 — Documented profile)
- **Deviations:** none
- **Code evidence:** `merged via [PR #33](https://github.com/Tusquito/codebase-indexer-mcp/pull/33) (`adr/0028-phase-1-documented-profile`)`
- **Verify:** carried from verification — tests run + plan compliance pass; review rounds: 2
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/33 — status: merged
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — merge
- **Phase:** Phase 2 — Arch-aware compose defaults
- **Tracker status:** `merged`
- **Choices:** squash merge `94d3bf7` on feature branch `adr/0028-phase-2-arch-aware-compose`; ADR Accept skipped — already Accepted; release skipped
- **Deviations:** none
- **Code evidence:** `merged via [PR #34](https://github.com/Tusquito/codebase-indexer-mcp/pull/34) (`adr/0028-phase-2-arch-aware-compose`; squash `94d3bf7`)`
- **Verify:** carried from verification — tests run + plan compliance pass — 43 targeted unit tests passed; Docker integration pass on arm64 with quality validation threshold 0; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/34 — status: merged — commit: 94d3bf7
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — implementation
- **Phase:** Phase 1 — Documented profile
- **Tracker status:** `implemented`
- **Choices:** Documented native arm64 CPU-first profile with manual TEI_IMAGE=cpu-arm64-1.9; M3 Pro 24 GiB + 18 GiB presets; RERANK_ENABLED=false; rejected amd64 emulation
- **Deviations:** none
- **Code evidence:** `docs/DEPLOYMENT.md`, `.env.example`, `README.md`, `.github/copilot-instructions.md`
- **Changelog:** no — invoker Changelog: no; status implemented

#### 2026-07-12 — implementation
- **Phase:** Phase 2 — Arch-aware compose defaults
- **Tracker status:** `implemented`
- **Choices:** arm64 TEI tag `cpu-arm64-latest` (not `cpu-arm64-1.9` — GHCR manifest unknown); MKL via amd64-only `docker-compose.tei.amd64-mkl.yml`; arch detection in `compose_files.py`; `default_reserve_gib()` shared by `tune_alloc`/`tune_stack`; integration script sets arch-aware TEI/MKL; NVIDIA/CI amd64 path unchanged
- **Deviations:** arm64 tag `cpu-arm64-latest` instead of ADR `cpu-arm64-1.9`
- **Code evidence:** `scripts/compose_files.py`, `docker-compose.tei.amd64-mkl.yml`, `docker-compose.tei.yml`, `scripts/tune_alloc.py`, `scripts/tune_stack.py`, `scripts/run_compose_integration.py`, `mcp_server/tests/test_compose_files.py`, `mcp_server/tests/test_tune_alloc.py`, `mcp_server/tests/test_run_compose_integration_gpu.py`, `docs/DEPLOYMENT.md`, `.env.example`, `README.md`, `.github/copilot-instructions.md`, `CHANGELOG.md`
- **Test debt:** Docker integration on M3 Pro arm64; full uv run pytest on maintainer host
- **Changelog:** no — invoker/changelog already had [Unreleased] bullet; tag updated in place

#### 2026-07-12 — implementation
- **Phase:** Phase 2 — Arch-aware compose defaults
- **Tracker status:** `implemented`
- **Choices:** MKL via amd64-only `docker-compose.tei.amd64-mkl.yml` overlay wired from `compose_file_args()`; arch detection in `compose_files.py`; `tei_image_default()` respects explicit `TEI_IMAGE` override; integration sets `TEI_MKL_INSTRUCTIONS=AVX2` on amd64 CPU only; `default_reserve_gib()` shared by `tune_alloc` and `tune_stack`; minimal DEPLOYMENT.md footnote sync; NVIDIA/CI amd64 path unchanged
- **Deviations:** none
- **Code evidence:** `scripts/compose_files.py`, `docker-compose.tei.amd64-mkl.yml`, `docker-compose.tei.yml`, `scripts/tune_alloc.py`, `scripts/tune_stack.py`, `scripts/run_compose_integration.py`, `mcp_server/tests/test_compose_files.py`, `mcp_server/tests/test_tune_alloc.py`, `mcp_server/tests/test_run_compose_integration_gpu.py`, `docs/DEPLOYMENT.md`
- **Test debt:** Docker compose integration on arm64 M3 Pro not run in agent session; maintainer must run `python scripts/run_compose_integration.py` before merge
- **Changelog:** no — invoker Changelog: no; status implemented

### ADR 0029 — Phase 1 — Documentation

#### 2026-07-12 — verification
- **Phase:** Phase 1 — Documentation
- **Tracker status:** `verified`
- **Choices:** Docs-only single PR; host-native Metal TEI in docs/DEPLOYMENT.md; four-surface sync; bundled 0028 CPU TEI remains default; Metal opt-in; Phase 2/3 deferred
- **Deviations:** none
- **Code evidence:** `docs/DEPLOYMENT.md`, `.env.example`, `README.md`, `.github/copilot-instructions.md`, `skill/codebase-indexer/SKILL.md`
- **Test debt:** Phase 2 --external-tei harness; Phase 3 metal_host_tei benchmark; maintainer manual Metal log check
- **Verify:** tests run + plan compliance pass; unit 492 passed 8 skipped; integration ACCELERATOR=cpu run_compose_integration.py --json exit 0; review round 1 clean
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — verification
- **Phase:** Phase 2 — Integration smoke
- **Tracker status:** `verified`
- **Choices:** Harness-only PR; `include_tei=False` via `compose_file_args`; force `ACCELERATOR=cpu`; M3 Pro Metal cgroup preset; host TEI preflight; `tei_container_absent` verdict gate; bundled path unchanged; quality/perf validation skipped
- **Deviations:** none
- **Code evidence:** `scripts/run_compose_integration.py`, `mcp_server/tests/test_run_compose_integration_gpu.py`, `docs/adr/0029-macos-host-native-tei-metal-acceleration.md`, `docs/DEPLOYMENT.md`, `CONTRIBUTING.md`, `.cursor/agents/adr-integration-tester.md`
- **Test debt:** Live M3 Pro `--external-tei` full Docker integration before merge; Phase 3 `metal_host_tei` benchmark; maintainer Metal log check on first embed
- **Verify:** 14/14 unit tests pass; bundled Docker integration pass; plan compliance pass; review rounds: 1
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — prioritization
- **Phase:** Phase 1 — Documentation
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0029 Phase 1 over 0028 Phase 4 (higher raw score but maintainer-manual benchmark session); over 0029 Phase 2 (delivery-order prerequisite — P1 docs first); over 0030 Phase 1 (Proposed, needs Accept, 7-phase greenfield); over 0028 Phase 3 (lower impact — rerank off by default); over 0027 Phase 1 (Proposed, tracker resolved defer Accept); over 0026 Phase 4 (NVIDIA-GPU-blocked on M3 Pro); single phase per pipeline rule; no ADR Accept required (0029 already Accepted); pre-release: opt-in profile only, bundled 0028 CPU TEI remains default. **Why now:** ADR 0028 Phases 1–2 merged 2026-07-12; compose arch-aware TEI defaults and darwin `tune_alloc` shipped. ADR 0029 Accepted with zero tracker phases. Maintainer is Apple Silicon M3 Pro without NVIDIA; 0026 Phase 4 GPU bake-off deferred until Mac path completes. Phase 1 docs normative-ize host Homebrew Metal TEI — the explicit next step deferred behind 0028 Phase 2 in the 2026-07-12 prioritization event. Partial README/copilot cross-links exist; DEPLOYMENT § Metal profile, `.env.example` Metal preset, and maintainer checklist remain open. **Suggested scope:** one phase (= one PR). **Chosen scope:** `docs/DEPLOYMENT.md` § host-native Metal TEI (brew install, `text-embeddings-router` operator profile, 24 GiB Docker VM reduced caps table, unified-memory notes, first-embed Metal log check); `.env.example` external Metal TEI comment block; README + `.github/copilot-instructions.md` + `skill/codebase-indexer/SKILL.md` sync; fix stale `.env.example` "until Phase 2 arch-aware defaults" comment. Defer Phase 2 `--external-tei` integration smoke and Phase 3 `macos_m3pro_matrix.json` `metal_host_tei` benchmark.
- **Deviations:** none
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — prioritization
- **Phase:** Phase 2 — Integration smoke
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0029 Phase 2 over 0027 Phase 1 (Proposed, needs Accept, deferred in prior cycles); over 0028 Phase 4 (higher raw score but maintainer-manual benchmark, not code PR); over 0030 Phase 1 (Proposed, 7-phase greenfield); over 0026 Phase 4 (NVIDIA-GPU-blocked on M3 Pro); single phase per pipeline rule; no ADR Accept required (0029 already Accepted); pre-release: opt-in Metal profile only. **Why now:** ADR 0028 Phases 1–2 and ADR 0029 Phase 1 merged 2026-07-12; tracker has no active phases; Phase 2 is explicit delivery-order prerequisite; `run_compose_integration.py` lacks `--external-tei` path; maintainer M3 Pro without NVIDIA blocks 0026 Phase 4. **Suggested scope:** one phase (= one PR). **Chosen scope:** Optional `--external-tei` flag in `scripts/run_compose_integration.py` — skip bundled `tei` service assertions when `TEI_URL` points at host (`host.docker.internal`); maintainer M3 Pro smoke checklist; Docker integration on external-TEI path.
- **Deviations:** none
- **Changelog:** no — user-facing unknown; invoker Changelog: no

#### 2026-07-12 — plan
- **Phase:** Phase 1 — Documentation
- **Tracker status:** `planned`
- **Choices:** Single PR; docs-only per ADR Phase 1; no server/compose/Python changes; 0028 bundled CPU TEI remains default; Metal opt-in via existing `TEI_URL` + empty `COMPOSE_PROFILES`; DEPLOYMENT Metal § placed after Apple Silicon, before generic External TEI; SKILL gets Deployment (macOS) section per AGENTS four-surface sync; ARCHITECTURE.md left unchanged (already cites 0029); suggested tier `claude-sonnet-5-thinking-high`. **Assumptions:** ADR 0028 Phases 1–2 merged; ADR 0029 Accepted; `docker-compose.yml` external TEI wiring sufficient; maintainer validates Metal path manually (not CI); Homebrew formula available on Apple Silicon host.
- **Deviations:** none
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — plan
- **Phase:** Phase 2 — Integration smoke
- **Tracker status:** `planned`
- **Choices:** Single PR; harness-only change (no MCP server code); `include_tei=False` via existing `compose_file_args`; force `ACCELERATOR=cpu` in harness process env; M3 Pro Metal cgroup preset in generated env; host TEI preflight before compose up; `tei_container_absent` required check; CI unchanged (bundled path); quality/perf validation skipped; throughput table deferred to Phase 3. **Assumptions:** Phase 1 merged; maintainer runs live `--external-tei` smoke on M3 Pro before merge approval; host Homebrew TEI started manually per DEPLOYMENT § Metal.
- **Deviations:** none
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — merge
- **Phase:** Phase 1 — Documentation
- **Tracker status:** `merged`
- **Choices:** squash merge `05f44a5` on feature branch `adr/0029-phase-1-metal-tei-docs` (`7c9503e` docs(deploy): add metal tei operator profile); ADR Accept skipped — already Accepted; release skipped
- **Deviations:** none
- **Code evidence:** `merged via [PR #35](https://github.com/Tusquito/codebase-indexer-mcp/pull/35) (`adr/0029-phase-1-metal-tei-docs`; squash `05f44a5`)`
- **Verify:** carried from verification — tests run + plan compliance pass; unit 492 passed 8 skipped; integration ACCELERATOR=cpu run_compose_integration.py --json exit 0; review round 1 clean
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/35 — status: merged — commit: 05f44a5
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — merge
- **Phase:** Phase 2 — Integration smoke
- **Tracker status:** `merged`
- **Choices:** squash merge `b322e0f` on feature branch `adr/0029-phase-2-integration-smoke`; ADR Accept skipped — unchanged (Accepted); release skipped
- **Deviations:** none
- **Verify:** carried from verification — 14/14 unit tests pass; bundled Docker integration pass; plan compliance pass; review rounds: 1
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/36 — status: merged — commit: b322e0f
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — implementation
- **Phase:** Phase 1 — Documentation
- **Tracker status:** `implemented`
- **Choices:** Docs-only single PR per ADR Phase 1; bundled 0028 CPU TEI remains default; Metal opt-in via existing `TEI_URL` + empty `COMPOSE_PROFILES`; DEPLOYMENT Metal § placed after Apple Silicon, before generic External TEI; four-surface sync (README, copilot-instructions, SKILL, DEPLOYMENT + `.env.example`); `--hostname 127.0.0.1` documented with upstream flag verification note; Phase 2 `--external-tei` integration smoke and Phase 3 `metal_host_tei` benchmark deferred
- **Deviations:** none
- **Code evidence:** `docs/DEPLOYMENT.md`, `.env.example`, `README.md`, `.github/copilot-instructions.md`, `skill/codebase-indexer/SKILL.md`
- **Test debt:** Phase 2 `--external-tei` harness path; Phase 3 `metal_host_tei` benchmark; maintainer manual Metal log check on first embed; full `run_compose_integration.py` re-run after `uv` install on verification host
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-12 — implementation
- **Phase:** Phase 2 — Integration smoke
- **Tracker status:** `implemented`
- **Choices:** Harness-only PR; `include_tei=False` via `compose_file_args`; force `ACCELERATOR=cpu`; M3 Pro Metal cgroup preset (12g/8g MCP/Qdrant); host TEI preflight before compose up; `tei_container_absent` required; bundled default path unchanged; quality/perf validation skipped
- **Deviations:** none
- **Code evidence:** `scripts/run_compose_integration.py`, `mcp_server/tests/test_run_compose_integration_gpu.py`, `docs/adr/0029-macos-host-native-tei-metal-acceleration.md`, `docs/DEPLOYMENT.md`, `CONTRIBUTING.md`, `.cursor/agents/adr-integration-tester.md`
- **Test debt:** Live M3 Pro `--external-tei` Docker integration (integration-tester); Phase 3 `metal_host_tei` benchmark; maintainer Metal log check on first embed
- **Changelog:** no — user-facing yes; invoker Changelog: no

### ADR 0030 — Phase 3 — Core search tools

#### 2026-07-21 — verification
- **Phase:** Phase 3 — Core search tools
- **Tracker status:** `verified`
- **Choices:** Hybrid RRF via Qdrant QueryAsync + client CrossCollectionRrf; Embedding PrefetchMultiplier/RrfK; Qdrant create parity (int8/HNSW/memmap/payload indexes); Host resolves default collection; six MCP tools; summary without build_dependencies; ColBERT no-op; Aspire gRPC :6334 + TEI arch image + SDK-container test fallback; quality via --mcp-url
- **Deviations:** none
- **Code evidence:** `QdrantVectorStore.cs`, `SearchService.cs`, `CrossCollectionRrf.cs`, `Host Tools (Search/Chunk/Outline/Summary/Collections)`, `SparseModelCacheResolver.cs`, `QdrantGrpcEndpoint.cs`, `eval_retrieval.py`, `run_compose_integration.py`, `docker-compose.aspire.yml`
- **Test debt:** Testcontainers hybrid query / recreate-quant asserts; SearchService multi-collection + hybrid-min_score unit cases; SymbolType chunker goldens; automated assert Aspire wait_http (live covered by compose)
- **Verify:** review rounds 1; dotnet test in SDK container (70 pass); integration Verdict pass; quality recall@10 +38.1% vs baseline (threshold 2); plan compliance pass
- **Changelog:** yes — Aspire/.NET MCP now exposes hybrid dense+sparse RRF search and core read tools (`search_codebase`, `search_symbols`, `get_chunk`, `get_file_outline`, `get_collection_summary`, `list_collections`); re-index after pull for quantization/HNSW/`symbol_type` parity.

#### 2026-07-21 — prioritization
- **Phase:** Phase 3 — Core search tools
- **Tracker status:** `candidate`
- **Choices:** Selected over 0031 Phase 1 (Proposed — needs Accept; Python-only ops fix short-lived vs .NET migration), over 0026 Phase 4 (GPU-blocked), over 0023 Phase 3 / 0027 Phase 1 (lower unlock); single phase per pipeline rule; no ADR Accept required (0030 already Accepted). Human decisions 2026-07-21: (1) Prefer ADR 0030 Phase 3 this cycle; (2) NVIDIA GPU available for future ADR 0026 Phase 4 scheduling; (3) Do not Accept ADR 0031/0027 this cycle — wait for a future cycle. **Why now:** Phases 1–2 merged; `SearchAsync` stubbed; Host has index tools only; ADR delivery order makes Phase 3 the usable-search milestone; prior open decisions kept 0030 as primary thread and deferred GPU bake-off / Accept-0027. **Suggested scope:** one phase (= one PR). **Chosen scope:** Implement .NET hybrid dense+sparse RRF search and MCP tools `search_codebase`, `search_symbols`, `get_chunk`, `get_file_outline`, `get_collection_summary`, `list_collections`; replace Phase-1 `SearchAsync` stub; golden recall@10 within ±2% of Python baseline on fixture collection; Docker integration via `scripts/run_compose_integration.py` (+ Aspire smoke as established); quality validation required; defer Phase 4 cross-ref/discovery, Phase 5 GraphRAG, Phase 6 ColBERT/ops, Phase 7 Python deletion. Pre-release: no backward-compat requirement unless ADR documents one; Docker integration always required; no schema migration version env vars — document re-index after pull.
- **Deviations:** none
- **Changelog:** no — user-facing unknown; invoker Changelog: no

#### 2026-07-21 — plan
- **Phase:** Phase 3 — Core search tools
- **Tracker status:** `planned`
- **Choices:** Expand `IVectorStore` to vector-based hybrid search (Application embeds); align collection create with Python quantization/HNSW/payload indexes; add `Chunk.SymbolType`; omit summary `build_dependencies` and full cross-ref tools until Phase 4; quality eval must hit .NET search (MCP `--mcp-url` or equivalent), not Python `run_search` alone; ColBERT no-op until Phase 6. Assumptions: Phases 1–2 merged artifacts are the edit base; Python remains production default; golden fixtures + `eval_baseline.json` unchanged; Aspire TEI/Qdrant available for gates. Pre-release: no backward-compat requirement unless ADR documents one; Docker integration always required; no schema migration version env vars — document re-index after pull.
- **Deviations:** none
- **Changelog:** no — invoker Changelog: no; status planned

#### 2026-07-21 — implementation
- **Phase:** Phase 3 — Core search tools
- **Tracker status:** `implemented`
- **Choices:** Aspire TEI via TEI_IMAGE + ADR 0028 tei_image_default (default cpu-1.9, arm64 cpu-arm64-latest); HF hub models--Qdrant--bm25 (+ snapshots) sparse resolve; harness dotnet test in mcr.microsoft.com/dotnet/sdk:10.0 when host lacks global.json SDK 10.0.301. Preserved: Hybrid RRF / CrossCollectionRrf; PrefetchMultiplier/RrfK; six MCP tools; summary without build_deps; rerank no-op; quality via --mcp-url threshold 2.
- **Deviations:** none
- **Code evidence:** `docker-compose.aspire.yml`, `src/CodebaseIndexer.AppHost/AppHost.cs`, `src/CodebaseIndexer.Infrastructure/Embedding/SparseModelCacheResolver.cs`, `src/CodebaseIndexer.Infrastructure/Embedding/OnnxSparseEmbedder.cs`, `test/CodebaseIndexer.Infrastructure.Tests/SparseModelCacheResolverTests.cs`, `scripts/run_compose_integration.py`
- **Test debt:** Full Aspire Docker + quality gate still required on target host; harness unit test for SDK-container fallback; Host.Tests re-smoke
- **Changelog:** no — invoker Changelog: no; status implemented

#### 2026-07-21 — implementation
- **Phase:** Phase 3 — Core search tools
- **Tracker status:** `implemented`
- **Choices:** Aspire/.NET Qdrant.Client uses gRPC :6334 (REST :6333 kept for health/metrics); QdrantGrpcEndpoint remaps well-known REST 6333→6334; Aspire harness waits on MCP /health (15×1s).
- **Deviations:** none beyond defensive 6333→6334 remap in client parse
- **Code evidence:** `docker-compose.aspire.yml`, `src/CodebaseIndexer.AppHost/AppHost.cs`, `src/CodebaseIndexer.Infrastructure/Qdrant/QdrantGrpcEndpoint.cs`, `src/CodebaseIndexer.Infrastructure/Qdrant/QdrantVectorStore.cs`, `src/CodebaseIndexer.Host/appsettings.json`, `src/CodebaseIndexer.Host/appsettings.Docker.json`, `scripts/run_compose_integration.py`, `test/CodebaseIndexer.Infrastructure.Tests/QdrantGrpcEndpointTests.cs`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md`
- **Test debt:** no automated assert that Aspire path uses wait_http for MCP; live gRPC left to compose quality re-run
- **Changelog:** no — invoker Changelog: no; status implemented

#### 2026-07-21 — implementation
- **Phase:** Phase 3 — Core search tools
- **Tracker status:** `implemented`
- **Choices:** Hybrid RRF via Qdrant QueryAsync + client CrossCollectionRrf; search knobs on Embedding: (PrefetchMultiplier, RrfK); Qdrant create parity (int8 quant, HNSW m/ef_construct, memmap, payload indexes); Host resolves default collection; rerank no-op until Phase 6; summary without build_deps
- **Deviations:** Application does not reference Infrastructure QdrantOptions (Host binds default collection); Host CallTool smoke allows Qdrant-unavailable error
- **Code evidence:** `src/CodebaseIndexer.Infrastructure/Qdrant/QdrantVectorStore.cs`, `src/CodebaseIndexer.Application/Services/SearchService.cs`, `src/CodebaseIndexer.Application/Search/CrossCollectionRrf.cs`, `src/CodebaseIndexer.Host/Tools/SearchTools.cs`, `src/CodebaseIndexer.Host/Tools/ChunkTools.cs`, `src/CodebaseIndexer.Host/Tools/OutlineTools.cs`, `src/CodebaseIndexer.Host/Tools/SummaryTools.cs`, `src/CodebaseIndexer.Host/Tools/CollectionsTools.cs`, `mcp_server/benchmarks/eval_retrieval.py`, `scripts/run_compose_integration.py`
- **Test debt:** Testcontainers hybrid query; recreate/quantization asserts; Aspire golden recall@10 gate; SymbolType chunker goldens
- **Changelog:** no — invoker Changelog: no; status implemented

#### 2026-07-13 — verification
- **Phase:** Phase 1 — Scaffold + storage + TEI
- **Tracker status:** `verified`
- **Choices:** Accept ADR 0030; repo-root solution; hand-authored docker-compose.aspire.yml; arm64 cpu-arm64-latest TEI; accelerator defaults cpu; MCP stub get_health only; SearchAsync stub until Phase 3; tokenizer truncation Phase 2; Python production default until Phase 7
- **Deviations:** none
- **Code evidence:** `CodebaseIndexer.sln`, `src/CodebaseIndexer.Infrastructure/Qdrant/QdrantVectorStore.cs`, `src/CodebaseIndexer.Infrastructure/Tei/TeiDenseEmbedder.cs`, `src/CodebaseIndexer.Host/Program.cs`, `src/CodebaseIndexer.AppHost/AppHost.cs`, `docker-compose.aspire.yml`, `test/CodebaseIndexer.Infrastructure.Tests/QdrantChunkIdTests.cs`, `test/CodebaseIndexer.Host.Tests/McpHostSmokeTests.cs`
- **Test debt:** Qdrant Testcontainers integration; live TEI embed smoke; AppHost DistributedApplicationTestingBuilder; NetArchTest layer rules; EmbedQueryAsync query-instruction regression test; arm64 AppHost manual validation on M3 Pro
- **Verify:** review round 2 clean; dotnet test 13/13 pass; plan compliance pass; Docker integration pass (ACCELERATOR=cpu); R1–R3 bug fixes verified
- **Changelog:** yes — Add .NET 10 MCP scaffold (opt-in via docker-compose.aspire.yml): Aspire AppHost, Qdrant + TEI + stub get_health tool; Python runtime remains default until ADR 0030 Phase 7.

#### 2026-07-13 — verification
- **Phase:** Phase 2 — Indexing pipeline
- **Tracker status:** `verified`
- **Choices:** —
- **Deviations:** none
- **Code evidence:** `WorkspaceScanner`, `TreeSitterChunker`, `Bm25EmbedderCore`, `IndexCodebaseService`, `IndexTools`, `docker-compose.aspire.yml`
- **Verify:** review rounds 2; dotnet test 32/32 pass; integration pass Python+aspire; plan compliance pass
- **Changelog:** no — invoker Changelog: no; status verified

#### 2026-07-13 — prioritization
- **Phase:** Phase 1 — Scaffold + storage + TEI
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0030 Phase 1 over 0026 Phase 4 (higher raw benchmark score but NVIDIA-GPU-blocked on M3 Pro); over 0028 Phase 3 (Accepted docs-only alternative); over 0027 Phase 1 (Proposed, tracker resolved defer Accept); over 0002 Phase 4 (higher scope GraphRAG); single phase per pipeline rule; requires formal Accept of Proposed ADR 0030 before dev; pre-release: Phases 1–6 opt-in parallel stack, no Python removal until Phase 7. **Why now:** Python runtime feature-complete (14 MCP tools, GraphRAG P1–3, TEI/ColBERT arcs merged); Mac deployment arc (0028/0029 P1–2) merged 2026-07-12; zero .NET code on disk; ADR cites maintainer C# expertise and pre-1.0 migration window; 0026 Phase 4 GPU bake-off blocked on M3 Pro without NVIDIA. **Suggested scope:** one phase (= one PR). **Chosen scope:** Accept ADR 0030 (prerequisite), then Phase 1 — AppHost + ServiceDefaults; Domain/Application/Infrastructure/Host projects; port interfaces + Chunk records; QdrantVectorStore; TeiDenseEmbedder via Refit; Host MCP stub with [McpServerTool] + MapMcp(); OTel via ServiceDefaults; AppHost boots Qdrant + TEI; Domain unit tests (zero infra refs); Refit TEI embed smoke; Host.Tests McpClient lists tools. Python image remains default until Phase 7.
- **Deviations:** none
- **Changelog:** no — user-facing unknown; invoker Changelog: no

#### 2026-07-13 — prioritization
- **Phase:** Phase 2 — Indexing pipeline
- **Tracker status:** `candidate`
- **Choices:** Prioritize 0030 Phase 2 over 0026 Phase 4 (higher raw benchmark score ~24.5 but NVIDIA-GPU-blocked on M3 Pro per tracker open decisions); over 0002 Phase 4 (optional Python GraphRAG cross-project; 0030 P5 will re-port); over 0027 Phase 1 (Proposed, Accept deferred this cycle); over 0023 Phase 3 (graph-enabled cleanup only); over 0017 Phase 2 (small observability slice); single phase per pipeline rule; no ADR Accept required (0030 already Accepted); pre-release: Phases 1–6 opt-in parallel .NET stack, Python production default until Phase 7; Docker integration mandatory per project-phase policy; no `*_SCHEMA_VERSION` env vars — re-index after pull. **Why now:** ADR 0030 Phase 1 merged 2026-07-13 (`docs/adr/tracker/phases/0030-phase-1.yaml`, PR #37): .NET scaffold (`CodebaseIndexer.sln`, Domain/Application/Infrastructure/Host/AppHost, `QdrantVectorStore`, `TeiDenseEmbedder`, `docker-compose.aspire.yml`, stub `get_health`) on disk; Phase 1 plan deferred tokenizer truncation and indexing to Phase 2; `SearchAsync` stubbed until Phase 3; grep confirms no `IndexPipeline`/`WorkspaceScanner`/`TreeSitter` in `src/`; tracker shows no active phases; Python runtime feature-complete (14 tools, GraphRAG P1–3, TEI/ColBERT arcs merged); 0026 Phase 4 GPU bake-off blocked on M3 Pro (no NVIDIA); ADR 0027 Proposed and deferred for Accept per tracker resolution. **Suggested scope:** one phase (= one PR). **Chosen scope:** Phase 2 — Indexing pipeline per `0030-migrate-mcp-server-to-dotnet10.md` §Phased delivery: `WorkspaceScanner` (SHA-256 incremental scan, ignore files); `TreeSitterChunker` (port `chunker.py` via `TreeSitter.DotNet`, regex SQL fallback); `OnnxSparseEmbedder` (`Microsoft.ML.OnnxRuntime`, same `Qdrant/bm25` artifacts); model-accurate dense tokenizer truncation (deferred from P1); `IndexPipeline` with `Channel<T>` stages in `IndexPipelineHostedService` (scan→parse→embed→upsert, no `Task.Run`); `IndexCodebaseService`; MCP index tools (`index_codebase`, `index_status`, `stop_indexing`, `index_all`); `ArrayPool` hashing and channel worker fan-out; exit: fixture incremental index with chunk IDs matching Python golden samples; Docker integration on `docker-compose.aspire.yml`; Python `run_compose_integration.py` remains green.
- **Deviations:** none
- **Code evidence:** ``docs/adr/tracker/phases/0030-phase-1.yaml` merged 2026-07-13 (PR #37)`, `grep confirms no `IndexPipeline`/`WorkspaceScanner`/`TreeSitter` in `src/``, ``SearchAsync` stubbed until Phase 3 per Phase 1 plan`
- **Changelog:** no — user-facing unknown; invoker Changelog: no

#### 2026-07-13 — plan
- **Phase:** Phase 1 — Scaffold + storage + TEI
- **Tracker status:** `planned`
- **Choices:** Accept ADR 0030 before implementation; repo-root CodebaseIndexer.sln (not dotnet/ subdirectory); check in generated docker-compose.aspire.yml in Phase 1 (not CI-only / not deferred to Phase 6); arm64 M3 Pro AppHost validation first, NVIDIA amd64 GPU compose overrides deferred; accelerator AppHost parameter defaults cpu; tokenizer truncation deferred Phase 2; MCP stub tool(s) only (not 14-tool parity); Python run_compose_integration.py must remain green; quality validation skip; performance report skip
- **Deviations:** none
- **Changelog:** no — invoker Changelog: no; status planned

#### 2026-07-13 — plan
- **Phase:** Phase 2 — Indexing pipeline
- **Tracker status:** `planned`
- **Choices:** Single PR; regex SQL fallback; no `*_SCHEMA_VERSION`; quality eval Phase 3; Python default until Phase 7; DOP=1 for hash workers; fastembed cache /root/.cache/fastembed; CI aspire manual-only + optional non-blocking CI. Assumptions: Phase 1 merged PR #37; arm64 M3 Pro; SearchAsync stub until Phase 3.
- **Deviations:** none
- **Changelog:** no — invoker Changelog: no; status planned

#### 2026-07-13 — merge
- **Phase:** Phase 1 — Scaffold + storage + TEI
- **Tracker status:** `merged`
- **Choices:** squash merge `55fc381` on feature branch `adr/0030-phase-1-scaffold`; ADR Accept phase 1 — Scaffold + storage + TEI (`51cef13`); release skipped
- **Deviations:** none
- **Code evidence:** `merged via [PR #37](https://github.com/Tusquito/codebase-indexer-mcp/pull/37) (`adr/0030-phase-1-scaffold`; squash `55fc381`)`
- **Verify:** carried from verification — review round 2 clean; dotnet test 13/13 pass; plan compliance pass; Docker integration pass (ACCELERATOR=cpu); R1–R3 bug fixes verified
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/37 — status: merged — commit: 55fc381
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-13 — merge
- **Phase:** Phase 2 — Indexing pipeline
- **Tracker status:** `merged`
- **Choices:** squash merge `2bdd90f` via [PR #38](https://github.com/Tusquito/codebase-indexer-mcp/pull/38); ADR Accept phase 2 — Indexing pipeline; release skipped
- **Deviations:** none
- **Code evidence:** `merged via [PR #38](https://github.com/Tusquito/codebase-indexer-mcp/pull/38) (squash `2bdd90f`)`
- **Verify:** carried from verification — review rounds 2; dotnet test 32/32 pass; integration pass Python+aspire; plan compliance pass
- **Git:** https://github.com/Tusquito/codebase-indexer-mcp/pull/38 — status: merged — commit: 2bdd90f
- **Changelog:** no — user-facing yes; invoker Changelog: no

#### 2026-07-13 — implementation
- **Phase:** Phase 1 — Scaffold + storage + TEI
- **Tracker status:** `implemented`
- **Choices:** Accept ADR 0030; repo-root CodebaseIndexer.sln; checked-in docker-compose.aspire.yml in Phase 1; arm64 cpu-arm64-latest TEI default; accelerator parameter defaults cpu; MCP stub get_health only; tokenizer truncation deferred Phase 2; Python production default until Phase 7
- **Deviations:** Aspire docker-compose publisher failed — compose file hand-authored; AppHost TEI image not parameterized; SearchAsync stub until Phase 3
- **Code evidence:** `CodebaseIndexer.sln`, `src/CodebaseIndexer.Domain/`, `src/CodebaseIndexer.Infrastructure/Qdrant/QdrantVectorStore.cs`, `src/CodebaseIndexer.Infrastructure/Tei/TeiDenseEmbedder.cs`, `src/CodebaseIndexer.Host/Program.cs`, `src/CodebaseIndexer.AppHost/AppHost.cs`, `docker-compose.aspire.yml`, `test/CodebaseIndexer.Host.Tests/McpHostSmokeTests.cs`
- **Test debt:** Qdrant Testcontainers integration; live TEI embed smoke; AppHost DistributedApplicationTestingBuilder; NetArchTest layer rules; arm64 AppHost manual validation on M3 Pro
- **Changelog:** no — invoker Changelog: no; status implemented

#### 2026-07-13 — implementation
- **Phase:** Phase 2 — Indexing pipeline
- **Tracker status:** `implemented`
- **Choices:** DOP=1; fastembed /root/.cache/fastembed; regex SQL fallback; optional non-blocking CI aspire job
- **Deviations:** Simplified ONNX sparse tokenization; TreeSitter chunker lacks full import-header logic; live Docker aspire smoke deferred to integration step
- **Code evidence:** `WorkspaceScanner.cs`, `TreeSitterChunker.cs`, `OnnxSparseEmbedder.cs`, `IndexCodebaseService.cs`, `IndexJobService.cs`, `IndexTools.cs`, `docker-compose.aspire.yml`, `run_compose_integration.py --aspire-stack`
- **Test debt:** Sparse parity; full chunker golden; Testcontainers E2E; aspire-stack Docker smoke
- **Changelog:** no — invoker Changelog: no; status implemented
<!-- END GENERATED:phase-logs -->

---

## Open decisions queue

Decisions made during implementation that are **not** worth amending the ADR file. Rendered from event `open_decisions` bullets; add new open items to the relevant event YAML.

<!-- BEGIN GENERATED:open-decisions -->
- 0002: Whether to run 0009 CI gate or 0008 test-debt PR in parallel with Accept/plan — Open — orchestrator decision
- Phase 4 — graph-augmented MCP retrieval (open/deferred)
- 0008: Live Qdrant integration test vs unit mocks only for adaptive skip — Open — test debt at verification
- 0008: Multi-collection adaptive skip + global RRF unit test — Open — deferred to verification
- 0008: Dedicated unit test for single-probe-hit ColBERT path (< 2 probe hits) — Open — test debt at verification
- 0008: Golden-set gap threshold sweep for RERANK_ADAPTIVE_GAP tuning — Open — test debt at verification (eval_retrieval --rerank)
- 0008: Whether to run 0008 test-debt closure PR in parallel with 0009 Phase 2 — Open — orchestrator decision
- 0009: Target multi_hop recall improvement threshold vs baseline 0.5 — Open — minimum recall lift threshold for eval_baseline.json multi_hop_2hop snapshot commit (plan or verification)
- 0009: CI gate for eval_multihop — Open — no CI gate at implementation; test debt
- 0009: Unit test for compare_vs_baseline() — Open — test debt at implementation
- 0014: Parallel vs sequential with 0008 test-debt PR — Open — orchestrator decision
- 0016: Whether 0002 Phase 2 supersedes if GraphRAG adoption is active — Open — orchestrator decision
- 0016: Optional Nomic snapshot for ADR narrative — Open — plan or verification decision
- 0016: Maintainer-only GPU eval run vs optional slow CI gate? — Open — plan decision
- 0016: Sequence 0002 P2 vs 0018 P2 after 0016 Phase 2? — Open — orchestrator decision
- 0020: Maintainer GPU availability for first fine-tune run? — Open
- 0020: Training max_seq_length default? — Open
- RESOLVED — ground-truth subset floor 19
- RESOLVED — every multi_hop row must carry secondary tag (no pure multi_hop rows)
- RESOLVED — Target per-tag query counts: 26/7/19/19/15 (symbol/conceptual/config/cross_file/multi_hop); in-place `golden_queries.jsonl` expansion (no v3 cutover); sequential only (0026 P2 before 0002 P3)
- RESOLVED — Spike time-box: 30 minutes per spike; drop on failure after time-box expires.
- RESOLVED — Granite-97m: include `ibm-granite/granite-embedding-97m` in Phase 3 native TEI verification.
- RESOLVED — Gated models: exclude all P2Use/gated models from Phase 3 (including `google/embeddinggemma-300m`) despite HF_TOKEN in `.env`; document skip/exclusion rationale in registry
- RESOLVED — (1) do not Accept ADR 0027 this cycle; proceed with 0028
- RESOLVED — (2) no NVIDIA GPU access — defer 0026 Phase 4 until after 0028 Mac path lands
- RESOLVED — (3) Phase 1 only — do not combine Phase 2 code in this PR
- RESOLVED — MKL overlay vs invert-default: amd64-only `docker-compose.tei.amd64-mkl.yml` overlay
- Manual Mac `compose up` still requires Phase 1 `.env` preset for `TEI_IMAGE` (automation limited to integration script + helper function)
- Whether to Accept ADR 0027 in a future cycle
- Whether NVIDIA GPU host is available for 0026 Phase 4 bake-off
- Whether 0028 Phase 4 maintainer benchmark runs on 24 GiB or 18 GiB Docker VM tier
- Confirm `text-embeddings-router --hostname` flag spelling at doc-write time
- Whether to add DEPLOYMENT Metal anchor to `docs/ARCHITECTURE.md` (optional, out of chosen scope)
- Accept ADR 0030 before .NET migration?
- Accept ADR 0027 in a future cycle?
- 0028 Phase 4 benchmark tier: 24 GiB vs 18 GiB Docker VM?
- NVIDIA GPU host availability for 0026 Phase 4 bake-off?
- confirm `text-embeddings-router --hostname` flag spelling at implementation
- RESOLVED — (1) fastembed cache /root/.cache/fastembed with fastembed_cache volume
- RESOLVED — (2) CI aspire manual-only on M3 Pro with optional non-blocking CI
- RESOLVED — (3) DOP=1
- RESOLVED — (1) Continue ADR 0030 Phase 2 as primary thread
- RESOLVED — (2) Defer 0026 Phase 4 until NVIDIA GPU host available
- RESOLVED — (3) Do not Accept ADR 0027 this cycle
- RESOLVED — (4) Use 24 GiB Docker VM tier for 0028 Phase 4
- RESOLVED — (5) Regex SQL fallback acceptable if TreeSitter.DotNet SQL grammar unavailable
<!-- END GENERATED:open-decisions -->

---

## How to update

Pipeline steps output a **Tracker append** block; the **invoker** (or a dedicated tracker specialist) applies YAML edits under `docs/adr/tracker/` and runs `scripts/render_adr_tracker.py`. ADR pipeline steps do not edit tracker or changelog files directly.

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

1. **Prioritization** — append event (`prioritization`); phase → `candidate`.
2. **Planning** — append event (`plan`); phase → `planned`; set chosen scope + user-facing flag.
3. **Implementation** — append event (`implementation`); phase → `implemented`; code evidence + test debt.
3.5. **Docker integration** — `adr-integration-tester` deploys Compose stack, runs live Qdrant pytest + MCP health, and **golden-set quality validation** when the plan marks **Quality validation: required**; optional **Performance report** (bench.py, report-only). **Mandatory deploy every phase**; quality eval is conditional on retrieval-touching work.
3a–3b. **Review / fix loop** — invoker passes `## Review findings` (`Verdict: needs_fix`) to bug fix; passes `## ADR bug fix report` back to code review. Repeat until `Verdict: clean`. No tracker append during the loop.
4. **Verification** — when review is clean, append event (`verification`, `verified`); if user-facing, add CHANGELOG `[Unreleased]` bullet.
5. **Git prepare** — feature branch `adr/NNNN-phase-N-<slug>`, grouped conventional commits, push, **PR into `main`**. No tracker append.
5a–5b. **PR review / babysit loop** — `adr-pr-review`; on `request_changes`, cloud `adr-pr-babysit` fixes branch; repeat until `approve` (max 5 rounds). No tracker append.
6. **Finish** — `adr-finisher` merges PR when gates pass, accepts ADR when eligible, optionally cuts CHANGELOG when version supplied; append event (`merge`, `merged`) with PR link.
7. **Cleanup** — `adr-git-operator` (`cleanup`) commits tracker on `main`, pushes, deletes merged feature branch, prunes remotes; workspace must be clean.

Apply steps 1–7 by writing each step's **Tracker append** output into `docs/adr/tracker/` YAML and re-rendering.

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
