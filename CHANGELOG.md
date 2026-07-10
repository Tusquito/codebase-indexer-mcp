# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

> **Note:** The MCP surface is **14 tools** when `RECOMMEND_ENABLED=true` (default). Historical release `[0.1.0]` below lists **12 tools** from before `recommend_code` shipped.

### Added

- **Opt-in Prometheus application metrics** ([ADR 0018](docs/adr/0018-telemetry-observability-otel-prometheus.md)) — set `METRICS_ENABLED=true` on MCP and ColBERT worker for `GET /metrics`; tool latency histograms, index/embed/memory counters, and deployment scrape documentation for MCP, ColBERT, and Qdrant; default unchanged (`METRICS_ENABLED=false`)
- **Optional GraphRAG Phase 1 (Neo4j code graph)** ([ADR 0002](docs/adr/0002-graphrag-neo4j-qdrant.md)) — enable with `GRAPH_ENABLED=true` and `docker-compose.neo4j.yml` to index a Neo4j code graph alongside Qdrant; disabled by default with no behavior change; full re-index required when enabling on existing collections
- **GraphRAG Phase 2 — Qdrant payload linking** ([ADR 0002](docs/adr/0002-graphrag-neo4j-qdrant.md)) — `GRAPH_ENABLED=true` now writes `graph_node_ids` neighbor links on each indexed Qdrant chunk and stamps collections `graph_enabled=true`; search warns once per collection lacking graph linkage; full re-index required when enabling on existing collections
- **Opt-in `expand_search_context` MCP tool** ([ADR 0002](docs/adr/0002-graphrag-neo4j-qdrant.md)) — graph-augmented retrieval: seeds from a `chunk_id` and expands the Neo4j code subgraph (bounded by `max_nodes`), returning a structured GraphContext JSON response; available only when `GRAPH_ENABLED=true`
- **`find_outlier_chunks` MCP tool** ([ADR 0014](docs/adr/0014-vector-discovery-and-ops-automation.md)) — find code chunks semantically distant from a module context via Qdrant Recommendation API (`BEST_SCORE`, negative-only) with cosine-to-centroid filtering; gated by `RECOMMEND_ENABLED` (no separate `OUTLIER_ENABLED`); configure via `OUTLIER_MAX_CONTEXT_SAMPLES` and `OUTLIER_MAX_SIMILARITY`. Dense-only single-collection; `limit` capped at 20. No re-index required.
- **`recommend_code` MCP tool** ([ADR 0014](docs/adr/0014-vector-discovery-and-ops-automation.md)) — find code chunks similar to positive examples and dissimilar from negative examples via Qdrant Recommendation API (dense-only, AVERAGE_VECTOR). Gated by `RECOMMEND_ENABLED` (default on); example count capped by `RECOMMEND_MAX_EXAMPLES`. Single-collection; optional `path_glob` post-filter. No re-index required.
- **Optional ColBERT HTTP sidecar** ([ADR 0015](docs/adr/0015-colbert-http-sidecar.md)) — `COLBERT_EMBED_BACKEND=remote` offloads ColBERT inference to the `colbert_worker` sidecar; default in-process ONNX unchanged; enable with `docker-compose.colbert-worker.yml` when `RERANK_ENABLED=true`
- **Optional GPU ColBERT sidecar** ([ADR 0015](docs/adr/0015-colbert-http-sidecar.md)) — `colbert_worker/Dockerfile.gpu` + `docker-compose.colbert-worker.gpu.yml` for faster index-time rerank embedding when using `COLBERT_EMBED_BACKEND=remote`; sidecar `/health` reports `device` and `cuda_available`; use `benchmarks/bench_colbert_sidecar.py` to compare CPU vs GPU sidecar throughput
- **Optional ColBERT reranking** ([ADR 0008](docs/adr/0008-optional-colbert-reranking.md)) — set `RERANK_ENABLED=true` to add a third multivector embed at index time and MAX_SIM rerank over hybrid prefetch candidates at query time; requires full re-index when enabling (`COLBERT_EMBED_MODEL`, `RERANK_PREFETCH`, `RERANK_MAX_QUERY_TOKENS`)
- **ColBERT rerank for cross-references and service map** ([ADR 0008](docs/adr/0008-optional-colbert-reranking.md)) — when `RERANK_ENABLED=true`, `find_cross_references` and `map_service_dependencies` now participate in ColBERT reranking (same hybrid prefetch → MAX_SIM path as `search_codebase` / `search_symbols`)
- **Adaptive ColBERT rerank skip** ([ADR 0008](docs/adr/0008-optional-colbert-reranking.md)) — when `RERANK_ENABLED=true`, probes hybrid RRF scores first and skips MAX_SIM rerank when the rank-1 vs rank-2 gap exceeds `RERANK_ADAPTIVE_GAP` (default `0.02`), reducing query latency on confident hybrid winners; tune via `RERANK_ADAPTIVE_ENABLED` / `RERANK_ADAPTIVE_GAP`
- **Per-call ColBERT rerank override** ([ADR 0008](docs/adr/0008-optional-colbert-reranking.md)) — optional `rerank=false` on `search_codebase`, `search_symbols`, `find_cross_references`, and `map_service_dependencies` skips ColBERT query embed and MAX_SIM rerank when `RERANK_ENABLED=true` (hybrid RRF only, lower latency)
- **Call-site cross-references** — chunks now store a `callees` payload (bare method names and `receiver.method` tokens). `find_cross_references` accepts optional `member` and `receiver` params and returns `call_site` matches via exact callee filter (not semantic search), including same-collection consumer links for inherited-field call sites (e.g. Spring `@Autowired` fields used in subclasses).
- **2-hop client eval harness** ([ADR 0009](docs/adr/0009-multi-hop-retrieval-strategies.md)) — `eval_multihop.py` benchmark for 2-hop client RRF evaluation on `multi_hop` golden queries; document CLI in `SEARCH_BEHAVIOR.md` and `ARCHITECTURE.md`
- **Compose integration GPU validation** ([ADR 0022](docs/adr/0022-gpu-default-cpu-fallback.md)) — `run_compose_integration.py` probes TEI health/embed/gpu when `ACCELERATOR=gpu`; unit tests in `test_run_compose_integration_gpu.py`

### Changed

- **TEI hard replace for dense embedding** ([ADR 0025](docs/adr/0025-huggingface-tei-dense-embedding.md)) — HuggingFace Text Embeddings Inference (TEI) replaces Ollama dense entirely; OpenAI `/v1/embeddings` via `TeiDenseBackend`; `docker-compose.tei.yml` + profile `bundled-tei`; removed `OLLAMA_*`, `DENSE_EMBED_BACKEND`, `ollama_dense.py`, and Ollama compose overlays. **Breaking:** full re-index required; set `COMPOSE_PROFILES=bundled-tei` and `DENSE_EMBED_MODEL` as HF repo id.

- **Graph call-site lookup (ADR 0023 Phase 2)** — when `GRAPH_ENABLED=true`, indexing omits `callees` from Qdrant payloads and stamps `graph_call_sites: true` collection metadata; `find_cross_references` Path D routes per collection (Neo4j for graph-ready collections, Qdrant scroll for others); force re-index when enabling graph on existing collections
- **Golden-set eval baseline** ([ADR 0021](docs/adr/0021-revert-jina-production-default-retire-qwen3.md)) — committed Jina @768 GPU eval baseline (`eval_baseline.json`) aligned with production defaults; frozen `eval_baseline_jina.json` gate reference preserved
- **CI accelerator split** ([ADR 0022](docs/adr/0022-gpu-default-cpu-fallback.md)) — GitHub Actions `ubuntu-latest` jobs set `ACCELERATOR=cpu` explicitly; `compose-integration` validates the CPU stack; optional non-blocking `gpu-smoke` on self-hosted GPU runners exercises the full GPU compose path
- **`compose-integration` non-blocking in CI** — GitHub Actions `compose-integration` now runs with `continue-on-error` since the full Docker Compose deploy adds 15min+ to every PR; it remains mandatory in the local ADR pipeline (`adr-integration-tester` runs it before every phase's code review)
- **Qwen3 experimental preset only** ([ADR 0021](docs/adr/0021-revert-jina-production-default-retire-qwen3.md)) — [ADR 0016](docs/adr/0016-qwen3-embedding-default-dense-model.md) default policy superseded; Qwen3 @1024 and [ADR 0020](docs/adr/0020-qwen3-code-finetune-jina-quality-gate.md) fine-tune track documented as opt-in/cancelled, not production default
- **GPU-default Docker Compose acceleration** ([ADR 0022](docs/adr/0022-gpu-default-cpu-fallback.md)) — GPU is now the default Docker Compose accelerator (`ACCELERATOR=gpu`); use `docker compose $(python scripts/compose_files.py)` for the canonical stack. Set `ACCELERATOR=cpu` explicitly for CPU-only hosts (CI, air-gapped servers).
- **ColBERT remote GPU sidecar default** ([ADR 0022](docs/adr/0022-gpu-default-cpu-fallback.md)) — when `RERANK_ENABLED=true`, ColBERT now defaults to the remote GPU sidecar (`COLBERT_EMBED_BACKEND=remote`); in-process ONNX requires explicit `COLBERT_EMBED_BACKEND=onnx` (intended for `ACCELERATOR=cpu` only)
- **Default dense embedding model** ([ADR 0021](docs/adr/0021-revert-jina-production-default-retire-qwen3.md)) — production default is **Jina Embeddings v2 base code** @768 via TEI (`jinaai/jina-embeddings-v2-base-code`); Qwen3 @1024 MRL remains an optional experimental preset with documented golden-set regression (−63.1% recall@10). Requires full re-index when changing model or dimensions
- **Model-accurate dense truncation** ([ADR 0017](docs/adr/0017-model-tokenizer-tei-dense-truncation.md)) — dense TEI truncation uses the HuggingFace model tokenizer (`DENSE_EMBED_MODEL`) instead of BM25 word-split approximation; set `HF_HOME` for persistent tokenizer cache in air-gapped deployments
- **Forced re-index required for `callees`** — existing collections need `index_codebase(..., force=True)` or `index_all(force=True)` to backfill `callees` and build the new keyword index; incremental re-index alone skips unchanged files and payloads are schemaless with no collection schema-version metadata

> **Historical (superseded by TEI — [ADR 0025](docs/adr/0025-huggingface-tei-dense-embedding.md)):** [ADR 0011](docs/adr/0011-ollama-only-dense-embedding.md) briefly made dense Ollama-only; [ADR 0016](docs/adr/0016-qwen3-embedding-default-dense-model.md) made Qwen3 the default before [ADR 0021](docs/adr/0021-revert-jina-production-default-retire-qwen3.md) reverted production default to Jina. Neither path is current — dense is TEI-only today.

### Removed

- In-process ONNX dense embedding, Ollama dense backend (`OLLAMA_*`, `docker-compose.ollama*.yml`), `embed_worker` remote backend, MCP CUDA/ROCm compose overrides (`docker-compose.gpu.yml`, `docker-compose.amd*.yml`, `docker-compose.embed-worker.yml`), and `EMBED_DEVICE` / `DENSE_EMBED_BACKEND=onnx|remote` configuration ([ADR 0025](docs/adr/0025-huggingface-tei-dense-embedding.md))

### Fixed

- **Member-only queries** — `find_cross_references` accepts `member` alone; no `query` or `symbol_name` required for exact call-site lookup.
- **Call-site match promotion** — when a chunk is already in results (e.g. from import search), the callees path promotes it to `call_site`; `top_k` no longer hides call sites behind import rows.
- **Code dependency links** — passing `symbol_name` with `member` populates `links[]` with `code_dependency` edges from call sites to the matching definition.
- **Cursor MCP connection** — document native HTTP transport (`"url": "http://localhost:8000/mcp"`) as the recommended client config; reconnects automatically after `mcp_server` restarts without a manual MCP reload. Deprecated `docker exec` into `codeindexer_mcp` (stdio pipe broke on every container restart).
- **`uv run` stdio startup** — removed `readme = "../README.md"` from `mcp_server/pyproject.toml` so `uv run` no longer fails with `OSError: Readme file does not exist` when re-syncing the editable package inside the container. Stdio fallback now uses the sidecar proxy (`codeindexer_proxy`) instead of exec into the main container.

## [0.1.0] - 2026-06-05

### Added

- **Hybrid semantic search** — dense ONNX embeddings (`DENSE_EMBED_MODEL`) fused with sparse keyword matching (`SPARSE_EMBED_MODEL`, default `Qdrant/bm25`) via reciprocal rank fusion (RRF) when `HYBRID_SEARCH=true`
- **AST-based chunking** — Tree-sitter parsing for Python, JavaScript, TypeScript, Go, Rust, Java, C, C++, and C#; sliding-window fallback for markup and unsupported languages
- **Incremental indexing** — SHA-256 and mtime pre-filters skip unchanged files; stale chunks purged after scan
- **12 MCP tools** — `index_codebase`, `index_status`, `index_all`, `stop_indexing`, `get_collection_summary`, `search_symbols`, `get_file_outline`, `search_codebase`, `get_chunk`, `list_collections`, `find_cross_references`, `map_service_dependencies`
- **Multi-arch Docker** — CPU (default), NVIDIA CUDA (`docker-compose.gpu.yml`), AMD ROCm native (`docker-compose.amd.yml`), and AMD ROCm WSL2 (`docker-compose.amd.wsl2.yml`) compose overrides *(removed in [Unreleased] / [ADR 0025](docs/adr/0025-huggingface-tei-dense-embedding.md); GPU acceleration is TEI + optional ColBERT sidecar today)*
- **Scheduled reindex** — `codeindexer_cron` service runs `cron/reindex.py` to git-fetch default branches and trigger incremental re-index when repos change
- **Token-efficient orientation tools** — `get_collection_summary`, `search_symbols`, and `get_file_outline` use Qdrant payload scroll only (zero embedding cost)
- **Cross-collection analysis** — `find_cross_references` and `map_service_dependencies` discover HTTP call chains and build-level dependencies (Maven, NuGet, npm, Gradle, Go, Cargo, Python)
- **stdio proxy** — thin JSON-RPC shim (`codebase_indexer.stdio_proxy`) forwards stdio clients to the in-container HTTP server without reloading models

[0.1.0]: https://github.com/your-org/codebase-indexer-mcp/releases/tag/v0.1.0
