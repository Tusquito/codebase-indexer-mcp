> **Historical record.** This ADR described the phased introduction of pluggable backends. **Backend selection is superseded by [0011](0011-ollama-only-dense-embedding.md)** — dense is Ollama-only today. The facade/protocol abstraction from Phase 1 remains in the codebase.

# 0001. Introduce pluggable embedding backends

- **Status:** Superseded
- **Date:** 2026-07-02
- **Deciders:** Maintainers
- **Superseded by:** [0011](0011-ollama-only-dense-embedding.md) — dense backend selection and Phase 3 embed-worker; Phase 1 facade/protocol abstraction remains in effect

## Context

Dense and sparse embedding today live in a single monolithic module ([`mcp_server/src/codebase_indexer/indexer/embedder.py`](../../mcp_server/src/codebase_indexer/indexer/embedder.py)):

- **Dense**: fastembed ONNX (CPU / CUDA / ROCm), with length-sorted batching, adaptive batch sizing, cgroup memory guards, and model release after indexing or idle timeout
- **Sparse**: fastembed BM25 in-process (always CPU), fused with dense via RRF when `HYBRID_SEARCH=true`

This design matches the product goal of a fully self-hosted, single-compose deployment with no external model server. It also couples embedding inference tightly to the MCP server process: GPU-specific Docker variants, ONNX memory tuning, and preload/release lifecycle all live in the same container as chunking, Qdrant I/O, and MCP tools.

Operators who already run **Ollama** for LLM workloads, or who want a **slim MCP container** with inference on a separate GPU host, have no supported path without forking. A full swap to Ollama is incomplete anyway — Ollama does not provide the BM25 sparse vectors required for hybrid search.

We need a decision that:

1. Preserves the current default (in-process ONNX, hybrid search, zero new dependencies)
2. Allows optional separation of dense embedding concerns over time
3. Avoids breaking the public `Embedder` API used by indexing, search, and cross-reference tools

## Decision

We will introduce a **pluggable backend architecture** for embedding, delivered in **three phases**. The public `Embedder` facade keeps its existing API (`embed_chunks`, `embed_query`, `embed_queries`, `release_models`); dense and sparse inference delegate to injectable backends selected via configuration.

### Phase 1 — Backend abstraction (behavior-preserving refactor)

Extract protocols and move current ONNX logic into dedicated backends. Default deployment unchanged.

| Module | Role |
|--------|------|
| `indexer/backends/base.py` | `DenseEmbedBackend` / `SparseEmbedBackend` protocols, shared `EmbeddingError` |
| `indexer/backends/onnx_dense.py` | Dense ONNX: providers, batch sorting, adaptive sizing, preload/release |
| `indexer/backends/onnx_sparse.py` | Sparse BM25 |
| `indexer/backends/factory.py` | `create_dense_backend(settings)`, `create_sparse_backend(settings)` |

`Embedder` becomes a facade: concurrent dense+sparse under normal load, sequential under memory pressure, idle timer unchanged. Class-level ONNX singletons move into the ONNX backends.

Config: `DENSE_EMBED_BACKEND=onnx` (only valid value in Phase 1).

### Phase 2 — Optional Ollama dense backend (opt-in)

Add `OllamaDenseBackend` calling `POST {OLLAMA_URL}/api/embed` via `httpx`, with HTTP batching, dimension validation, retries, and preload health checks.

- **Sparse stays in-process ONNX** — hybrid search preserved
- `DENSE_EMBED_BACKEND=ollama` selects Ollama for dense only
- `EMBED_DEVICE` applies to ONNX dense only; ignored when dense backend is Ollama
- Optional `docker-compose.ollama.yml`; MCP image still includes fastembed for sparse
- Full re-index required when switching dense backend or model

### Phase 3 — Optional embed-worker sidecar (opt-in)

Add a dedicated `embed_worker` HTTP service reusing the same ONNX backends and batch semantics. MCP uses `RemoteEmbedBackend` when `DENSE_EMBED_BACKEND=remote`; both dense and sparse route to the worker.

- Endpoints: `GET /health`, `POST /v1/embed/dense`, `POST /v1/embed/sparse`, `POST /v1/embed/hybrid`
- Optional slim MCP image without ONNX when remote backend is configured
- Shared backend code between worker and MCP (no duplicated batch logic)

### Cross-phase invariants

1. Public `Embedder` API unchanged — no changes to search tools, pipeline, or MCP tool signatures
2. Default remains `DENSE_EMBED_BACKEND=onnx`; existing Docker GPU matrix untouched
3. Hybrid search default preserved unless user disables `HYBRID_SEARCH`
4. Collection metadata extended to record `dense_embed_backend`; warn on backend mismatch
5. Re-index required when switching backend or dense model

### Delivery order

Ship Phase 1 alone first. Phases 2 and 3 follow as separate PRs once the interface is stable.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Phased pluggable backends (chosen)** | Default unchanged; clean extension points; Ollama and worker both supported without one forcing the other | Multi-phase effort; sparse ONNX remains in MCP for Ollama path |
| **Move all embedding to Ollama** | Simple operational model if Ollama already deployed | No BM25 sparse → loses hybrid search or requires second system anyway; HTTP latency on bulk index; model ecosystem mismatch vs fastembed ONNX |
| **Dedicated embed worker only (skip Ollama)** | Full separation with same ONNX models and batch semantics | No benefit for users who already standardize on Ollama; larger initial scope |
| **Keep monolithic embedder indefinitely** | Zero refactor cost | No path for separation of concerns; GPU/ONNX complexity stays in MCP forever |
| **OpenAI-compatible remote API as first step** | Familiar API | External dependency conflicts with self-hosted goal; deferred |

## Consequences

### Positive

- Default users see no behavior or deployment change
- Dense inference can move out of MCP for operators who need modularity (Ollama or worker)
- ONNX optimizations (batch sorting, memory guards) stay encapsulated in backends
- Future backends (e.g. OpenAI-compatible) can plug into the same interface
- Phase 3 enables slim MCP + GPU worker topology without Ollama model-format constraints

### Negative / trade-offs

- Ollama path adds HTTP latency per search and per index batch; bulk indexing throughput may drop vs in-process ONNX
- Ollama path still requires fastembed/onnxruntime in MCP for sparse BM25 — partial separation only
- Remote worker adds a fourth service, network failure mode, and dual memory pools to tune
- Backend switch requires full re-index; no automatic migration orchestration in scope
- Phase 1 refactor touches core embedder code — regression risk despite behavior-preserving intent

### Neutral / follow-ups

- OpenAI-compatible dense backend (`dense_embed_backend=openai`) deferred
- Automatic re-index on backend switch deferred
- Removing ONNX from default MCP image deferred to Phase 3 slim variant only

## Implementation notes

### Affected paths

- `mcp_server/src/codebase_indexer/indexer/embedder.py` — facade refactor
- `mcp_server/src/codebase_indexer/indexer/backends/` — new package
- `mcp_server/src/codebase_indexer/config.py` — `DENSE_EMBED_BACKEND`, Ollama/worker settings
- `mcp_server/src/codebase_indexer/context.py` — factory wiring
- `mcp_server/src/codebase_indexer/main.py` — preload/release per backend
- `mcp_server/src/codebase_indexer/storage/qdrant.py` — collection metadata for backend
- Phase 2: `docker-compose.ollama.yml`, `.env.example`, README, ARCHITECTURE
- Phase 3: `embed_worker/`, `docker-compose.embed-worker.yml`, optional slim MCP Dockerfile stage

### Configuration (cumulative)

| Variable | Phase | Default | Values |
|----------|-------|---------|--------|
| `DENSE_EMBED_BACKEND` | 1→3 | `onnx` | `onnx`, `ollama` (2), `remote` (3) |
| `OLLAMA_URL` | 2 | `http://host.docker.internal:11434` | Ollama base URL |
| `OLLAMA_EMBED_MODEL` | 2 | derived from `DENSE_EMBED_MODEL` | Ollama model name |
| `OLLAMA_EMBED_BATCH_SIZE` | 2 | `32` | HTTP batch size |
| `OLLAMA_TIMEOUT` | 2 | `120` | Seconds per batch |
| `EMBED_WORKER_URL` | 3 | `http://embed_worker:8081` | Worker base URL |
| `EMBED_WORKER_TIMEOUT` | 3 | `300` | HTTP timeout |

### Rollout

- Phase 1: default unchanged, no Docker/README changes required
- Phase 2–3: opt-in via compose overrides and env vars

### Re-index

**Yes** — required when changing `DENSE_EMBED_BACKEND`, dense model, or vector dimension.

## Validation

| Phase | Checks |
|-------|--------|
| 1 | Existing embedder tests pass unchanged; Ollama dense tests in `test_ollama_dense_backend.py`; zero diff in default Docker behavior |
| 2 | `test_ollama_dense_backend.py` with HTTP mocks; factory selects Ollama backend; hybrid search end-to-end with mocked Ollama + sparse ONNX |
| 3 | Worker unit tests for batch endpoints; `RemoteEmbedBackend` integration tests; optional compose smoke test indexing a small corpus |

Success criteria:

- Default deployment (`DENSE_EMBED_BACKEND=onnx`) matches current indexing throughput and search latency within test noise
- Ollama and remote paths produce vectors of `DENSE_EMBED_VECTOR_SIZE` and integrate with existing Qdrant hybrid search
- No changes to MCP tool contracts or caller code outside the embedder layer
