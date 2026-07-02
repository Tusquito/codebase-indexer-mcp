# 0011. Ollama-only dense embedding

- **Status:** Accepted
- **Date:** 2026-07-02
- **Deciders:** Maintainers
- **Supersedes:** [0001](0001-pluggable-embed-backends.md) — backend selection (`onnx` / `ollama` / `remote`) and Phase 3 embed-worker scope
- **Related:** [0006](0006-explicit-fastembed-pipeline.md) — sparse BM25 pipeline unchanged

## Context

[ADR 0001](0001-pluggable-embed-backends.md) introduced a phased pluggable backend architecture:

- **Phase 1:** ONNX dense + ONNX sparse in-process (default)
- **Phase 2:** Optional Ollama HTTP for dense; sparse BM25 stays in MCP
- **Phase 3:** Optional `embed_worker` sidecar with `DENSE_EMBED_BACKEND=remote` and slim MCP image

After implementing all three phases, operational experience showed:

1. **Separation of concerns** — the primary motivation for moving dense embedding out of MCP was GPU memory isolation and reuse of an existing Ollama stack, not maintaining three interchangeable dense providers.
2. **Hybrid search constraint** — Ollama does not provide BM25-style sparse vectors. Sparse encoding must remain in MCP regardless of dense backend, so the MCP container always needs fastembed/onnxruntime for `SPARSE_EMBED_MODEL`.
3. **Maintenance cost** — supporting in-process ONNX dense (CPU/CUDA/ROCm Dockerfile matrix), Ollama dense, and remote ONNX worker duplicated batch semantics, tests, compose files, and documentation without a clear default operator path.
4. **Embed-worker redundancy** — once dense is Ollama-only, the remote worker existed only to run ONNX dense+sparse that Ollama could not replace for dense anyway; slim MCP (`MCP_SLIM=1`) saved little because sparse ONNX still required the full fastembed stack in MCP.

We need a decision that:

1. Makes **Ollama the sole dense vector source** — no in-process ONNX dense, no remote dense proxy
2. Preserves **hybrid search** (Ollama dense + in-process BM25 sparse)
3. Keeps the **Embedder facade and backend protocols** from ADR 0001 Phase 1
4. Simplifies deployment to one GPU story: **Ollama GPU** via `docker-compose.ollama.gpu.yml`, not MCP CUDA/ROCm images

## Decision

We will **remove all non-Ollama dense embedding paths** and treat Ollama HTTP as the only supported dense encoder.

### In scope

| Component | Outcome |
|-----------|---------|
| Dense backend | Always `OllamaDenseBackend` (`indexer/backends/ollama_dense.py`) |
| Sparse backend | Always `OnnxSparseBackend` (BM25) in MCP |
| Factory | `create_dense_backend()` always returns Ollama; no `DENSE_EMBED_BACKEND` switch |
| Config | `dense_embed_backend` fixed to `"ollama"`; Ollama URL/model/batch/timeout env vars retained |
| Docker MCP image | Single CPU slim stage — no `EMBED_DEVICE`, `MCP_SLIM`, CUDA/ROCm builder matrix |
| Compose | `docker-compose.ollama.yml` (+ optional `.ollama.gpu.yml`); remove `gpu.yml`, `amd*.yml`, `embed-worker.yml` |
| Removed code | `onnx_dense.py`, `remote.py`, `embed_worker/` package |

### Out of scope

- Replacing sparse BM25 with an external service (would break hybrid search or add a second HTTP dependency on every query)
- Official Ollama images for every HuggingFace dense model — operators use community Ollama ports (e.g. `unclemusclez/jina-embeddings-v2-base-code`) or host-native Ollama
- Automatic re-index orchestration when `OLLAMA_EMBED_MODEL` changes

### Configuration

| Variable | Role |
|----------|------|
| `OLLAMA_URL` | MCP → Ollama base URL |
| `OLLAMA_EMBED_MODEL` | Model tag for `/api/embed` (defaults derived from `DENSE_EMBED_MODEL` basename if unset) |
| `OLLAMA_EMBED_BATCH_SIZE` | HTTP batch size |
| `OLLAMA_TIMEOUT` | Per-request timeout (seconds) |
| `MAX_DENSE_EMBED_TOKENS` | Caps text sent to Ollama before `/api/embed` (word-split approximation; `0` = auto from `DENSE_EMBED_MODEL` registry) |
| `DENSE_EMBED_MODEL` | Metadata / dimension validation registry; not used for in-process inference |
| `DENSE_EMBED_VECTOR_SIZE` | Qdrant dense vector dimension; must match Ollama model output |
| `COMPOSE_PROFILES=bundled-ollama` | Start bundled Ollama service in Compose |
| `OLLAMA_GPU` + `docker-compose.ollama.gpu.yml` | NVIDIA GPU for bundled Ollama only |

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Ollama-only dense (chosen)** | Single dense path; GPU via Ollama; aligns with ops running Ollama for LLMs; removes Dockerfile/compose matrix | HTTP latency vs in-process ONNX; requires Ollama running; community model ports may differ slightly from ONNX weights |
| **Keep ADR 0001 three-backend model** | Flexibility for ONNX-only deployments without Ollama | Three code paths, three compose stories, ongoing test/doc burden; embed-worker redundant once Ollama is primary |
| **ONNX-only (revert Phase 2–3)** | Lowest latency; no Ollama dependency | Rejects separation-of-concerns goal; GPU complexity stays in MCP |
| **Move sparse to Ollama/worker too** | Theoretically slim MCP | Ollama has no BM25 sparse; would lose hybrid search or require custom sparse service |

## Consequences

### Positive

- One dense deployment model: configure Ollama, pull model, index
- MCP image and Dockerfile simplified (CPU-only; sparse ONNX only)
- GPU acceleration consolidated in Ollama container (`docker-compose.ollama.gpu.yml`)
- Removed embed-worker service, remote HTTP client, and ONNX dense device/provider logic
- Factory and tests shrink to Ollama + sparse paths

### Negative / trade-offs

- **Breaking change** — deployments using `DENSE_EMBED_BACKEND=onnx` or `remote` must migrate to Ollama and **full re-index**
- **Ollama required** — MCP cannot index or search without reachable Ollama for dense vectors
- **Bulk indexing throughput** — HTTP batching to Ollama may be slower than in-process ONNX on the same hardware
- **Model parity** — Ollama community quantizations may not match prior ONNX/Jina vectors bit-for-bit
- **Sparse still in MCP** — fastembed/onnxruntime remain in the MCP image for BM25; not a fully inference-free MCP container

### Neutral / follow-ups

- OpenAI-compatible dense backend remains deferred
- Automatic re-index on model change remains deferred
- ADR 0006 explicit FastEmbed pipeline applies to **sparse** only; dense path is httpx → Ollama

## Implementation notes

### Affected paths

- `mcp_server/src/codebase_indexer/indexer/backends/factory.py` — always Ollama dense
- `mcp_server/src/codebase_indexer/indexer/embedder.py` — facade without ONNX dense / remote branches
- `mcp_server/src/codebase_indexer/config.py` — remove `embed_device`, embed-worker settings; `dense_embed_backend: Literal["ollama"]`
- `mcp_server/Dockerfile` — single CPU builder/runtime
- `docker-compose.yml` — Ollama env vars on `mcp_server`; no `EMBED_DEVICE`
- Deleted: `onnx_dense.py`, `remote.py`, `embed_worker/`, `docker-compose.gpu.yml`, `docker-compose.amd*.yml`, `docker-compose.embed-worker.yml`

### Rollout

**Breaking.** Operators must:

1. Run Ollama (bundled via `docker-compose.ollama.yml` or external on host)
2. Set `OLLAMA_EMBED_MODEL` (and matching `DENSE_EMBED_VECTOR_SIZE`)
3. `ollama pull <model>`
4. **Force re-index** all collections

### Re-index

**Yes** — required when migrating from ONNX or remote dense vectors, or when changing `OLLAMA_EMBED_MODEL` / vector dimension.

## Validation

- `test_factory.py` — factory always selects `OllamaDenseBackend` + `OnnxSparseBackend`
- `test_ollama_dense_backend.py` — HTTP mock behavior unchanged
- `test_config.py` — rejects non-`ollama` `dense_embed_backend`
- Docker build: single-stage CPU image builds successfully
- Operational: MCP logs `ollama_embed_ready`; `docker exec codeindexer_ollama ollama ps` shows GPU when using `.ollama.gpu.yml`

Success criteria:

- No code path loads in-process ONNX dense models
- Hybrid search (Ollama dense + BM25 sparse) produces indexed collections searchable via existing MCP tools
- Documentation and `.env.example` describe Ollama as the only dense source
