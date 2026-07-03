# Deployment

Docker Compose runs Qdrant, the MCP server, and the cron reindex job. **Dense embedding always goes through Ollama**; sparse BM25 stays in-process in the MCP container. Configuration is env-var driven via `.env` (copy from `.env.example`).

## Compose files

| File | Purpose |
|------|---------|
| `docker-compose.yml` | Base stack: `codeindexer_qdrant`, `codeindexer_mcp`, `codeindexer_cron` |
| `docker-compose.ollama.yml` | Optional bundled `ollama` service (`COMPOSE_PROFILES=bundled-ollama`) |
| `docker-compose.ollama.gpu.yml` | NVIDIA GPU for bundled Ollama — use when `OLLAMA_GPU=1` in `.env` |
| `docker-compose.colbert-worker.yml` | Optional ColBERT HTTP sidecar ([ADR 0015](adr/0015-colbert-http-sidecar.md)) |
| `docker-compose.colbert-worker.gpu.yml` | NVIDIA GPU for ColBERT sidecar — use when `COLBERT_GPU=1` in `.env` |

### Docker Compose env passthrough

Compose reads your host `.env` at `docker compose up` time and injects variables into containers **explicitly** (there is no blanket `env_file: .env`). Every `Settings` field in `config.py` is wired through `docker-compose.yml` with `${VAR:-default}` so uncommented entries in `.env.example` take effect after `docker compose up -d` (or `docker compose restart mcp_server` for env-only changes).

| Service | Source | Notes |
|---------|--------|-------|
| `mcp_server` | `docker-compose.yml` | All application `Settings` env vars |
| `mcp_server` | `docker-compose.ollama.yml` | Overrides `OLLAMA_*` when bundled/external Ollama profile is used |
| `cron` | `docker-compose.yml` | `INDEX_TIMEOUT`, `MCP_HTTP_TIMEOUT`, `GIT_TIMEOUT`, `MCP_URL` |
| `qdrant` / `ollama` | compose only | Resource caps and Ollama service env — not Python `Settings` |

**Not in `.env`:** `dense_embed_backend` is fixed to `ollama` in code ([ADR 0011](adr/0011-ollama-only-dense-embedding.md)). `FASTEMBED_CACHE_PATH` is set to the container cache volume path.

**Compose-only variables** (not read by Python `Settings`): `WORKSPACE_ROOT`, `MCP_MEM_LIMIT`, `QDRANT_MEM_LIMIT`, `MCP_CPUS`, `QDRANT_CPUS`, `COMPOSE_PROFILES`, `OLLAMA_GPU`, `OLLAMA_GPU_COUNT`, `OLLAMA_PORT`, `OLLAMA_MEM_LIMIT`, `OLLAMA_CPUS`, `COLBERT_GPU`, `COLBERT_GPU_COUNT`, `COLBERT_MEM_LIMIT`, `COLBERT_CPUS`.

For local `uv run python -m codebase_indexer.main`, pydantic reads `.env` in `mcp_server/` directly — same variable names apply.

Run Ollama natively or in your own container on `127.0.0.1:11434`. Leave `COMPOSE_PROFILES` unset.

`.env`:

```env
OLLAMA_URL=http://host.docker.internal:11434
OLLAMA_EMBED_MODEL=unclemusclez/jina-embeddings-v2-base-code
DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code
DENSE_EMBED_VECTOR_SIZE=768
```

```bash
docker compose -f docker-compose.yml -f docker-compose.ollama.yml up -d --build
docker exec codeindexer_ollama ollama pull unclemusclez/jina-embeddings-v2-base-code
docker compose restart mcp_server
```

### Bundled Ollama in Compose (recommended)

`.env`:

```env
COMPOSE_PROFILES=bundled-ollama
OLLAMA_URL=http://ollama:11434
OLLAMA_EMBED_MODEL=unclemusclez/jina-embeddings-v2-base-code
DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code
DENSE_EMBED_VECTOR_SIZE=768
OLLAMA_GPU=0
```

```bash
docker compose -f docker-compose.yml -f docker-compose.ollama.yml up -d --build
docker exec codeindexer_ollama ollama pull unclemusclez/jina-embeddings-v2-base-code
docker compose restart mcp_server
```

### Ollama GPU (bundled service only)

Requires NVIDIA driver + [Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html).

`.env`:

```env
COMPOSE_PROFILES=bundled-ollama
OLLAMA_GPU=1
OLLAMA_GPU_COUNT=1
OLLAMA_EMBED_MODEL=unclemusclez/jina-embeddings-v2-base-code
DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code
DENSE_EMBED_VECTOR_SIZE=768
```

```bash
docker compose -f docker-compose.yml -f docker-compose.ollama.yml \
  -f docker-compose.ollama.gpu.yml up -d --build
docker exec codeindexer_ollama ollama pull unclemusclez/jina-embeddings-v2-base-code
```

Verify GPU: `docker exec codeindexer_ollama ollama ps` — `PROCESSOR` should show `GPU` while the model is loaded. CPU-only shows `100% CPU`.

| Variable | Default | Role |
|----------|---------|------|
| `COMPOSE_PROFILES` | *(empty)* | Set to `bundled-ollama` to start the Compose-managed Ollama service |
| `OLLAMA_URL` | `http://host.docker.internal:11434` (base compose); `http://ollama:11434` when `-f docker-compose.ollama.yml` is merged | MCP → Ollama base URL; set explicitly in `.env` for your setup |
| `OLLAMA_GPU` | `0` | Document only — set `1` and add `docker-compose.ollama.gpu.yml` to enable NVIDIA GPU |
| `OLLAMA_GPU_COUNT` | `1` | GPUs reserved for bundled Ollama when using `.ollama.gpu.yml` |
| `OLLAMA_EMBED_MODEL` | *(from `.env`)* | Ollama model tag for dense embedding (must match `DENSE_EMBED_VECTOR_SIZE`) |
| `OLLAMA_EMBED_BATCH_SIZE` | `32` | Texts per Ollama `/api/embed` request |
| `OLLAMA_TIMEOUT` | `120` | HTTP timeout (seconds) for Ollama calls |
| `OLLAMA_PORT` | `11434` | Host port when bundled Ollama publishes to loopback |
| `OLLAMA_MEM_LIMIT` | `4g` | cgroup memory cap for bundled Ollama |
| `OLLAMA_CPUS` | `4` | CPU limit for bundled Ollama |

Verify MCP: `curl http://localhost:8000/health` and logs show `ollama_embed_ready`. If Ollama is down at startup, MCP still serves `/health` but logs `model_preload_failed_continuing` until Ollama is reachable (restart `mcp_server` after Ollama is ready).

**Full re-index required** after changing `OLLAMA_EMBED_MODEL` or `DENSE_EMBED_VECTOR_SIZE`. See [ADR 0011](adr/0011-ollama-only-dense-embedding.md).

Sparse embedding (`SPARSE_EMBED_MODEL`, default BM25) always runs on **CPU** inside MCP.

## Memory and CPU tuning

Required resource caps (Docker Compose — not read by Python directly):

| Variable | Role |
|----------|------|
| `MCP_MEM_LIMIT` / `MCP_CPUS` | MCP container cap |
| `QDRANT_MEM_LIMIT` / `QDRANT_CPUS` | Qdrant container cap |
| `OMP_NUM_THREADS` | ONNX/BLAS threads for sparse BM25 |
| `OLLAMA_MEM_LIMIT` / `OLLAMA_CPUS` | Bundled Ollama service caps |

Pipeline knobs (see `.env.example` presets):

| Goal | Knobs |
|------|-------|
| More CPU | Raise `OMP_NUM_THREADS`, `BATCH_SIZE`, `OLLAMA_CPUS`; reserve cores for Qdrant via `QDRANT_CPUS` |
| Lower RAM | Lower `BATCH_SIZE`, `FLUSH_EVERY`, `MAX_DENSE_EMBED_TOKENS`; enable `SEQUENTIAL_EMBED` |
| Faster search | Tune `HNSW_EF`, `PREFETCH_MULTIPLIER`; disable `VECTORS_ON_DISK` if RAM allows |

### ColBERT rerank: Qdrant upsert batching

When `RERANK_ENABLED=true`, each point carries a **ColBERT multivector** (hundreds of 128-d token vectors per chunk, plus dense, sparse, and payload). Upserts go to Qdrant over **HTTP**; a single request body can exceed what the client or server accepts.

**Symptom:** indexing logs show `upsert_retry` / `prev_upsert_error` with an **empty** `error=` field, or `ResponseHandlingException` / `httpx.ReadError` in debug output. Qdrant access logs may show `PUT .../points` **400** or the connection may drop mid-response. Failed upserts leave **gaps** in the collection (`points_count` < `total_chunks`) and MCP RSS climbs because `trim_memory` does not run until upsert succeeds.

**Cause:** `UPSERT_BATCH` too large for ColBERT payload size (not a schema or dimension mismatch). The default **`UPSERT_BATCH=500`** is fine for dense+sparse only; with rerank enabled, use a **much smaller** sub-batch.

**Mitigation:**

| Knob | Dense+sparse only | ColBERT rerank enabled |
|------|-------------------|-------------------------|
| `UPSERT_BATCH` | `50`–`500` | **`10`–`25`** (start at `10`) |
| `FLUSH_EVERY` | up to `1500` | **`64`–`128`** (MCP holds full flush until upsert) |
| `COLBERT_EMBED_BATCH_SIZE` | — | `16`–`32` (sidecar HTTP batching; independent of upsert) |

The MCP upsert path retries up to **5 times** with exponential backoff on transient failures (`qdrant.py`). If errors persist, lower `UPSERT_BATCH` first.

**Remote ColBERT sidecar** ([ADR 0015](adr/0015-colbert-http-sidecar.md)): offloading inference does **not** shrink upsert payloads — MCP still holds dense + sparse + returned ColBERT multivectors per flush until Qdrant accepts them. Tune `UPSERT_BATCH` the same way.

**Verified preset** (Ollama GPU + ColBERT sidecar + rerank, ~16 GB host): see `.env.example` sidecar block — `UPSERT_BATCH=10`, `FLUSH_EVERY=96`, `MCP_MEM_LIMIT=3g`, `COLBERT_MEM_LIMIT=3g`.

See [SEARCH_BEHAVIOR.md](SEARCH_BEHAVIOR.md#optional-colbert-reranking-rerank_enabledtrue) for search-path details.

### ColBERT GPU sidecar

Optional GPU acceleration for the ColBERT HTTP sidecar ([ADR 0015](adr/0015-colbert-http-sidecar.md) phase 2). The MCP container stays on CPU fastembed/onnxruntime; only the sidecar image swaps to `fastembed-gpu` + `onnxruntime-gpu==1.26.0` via `colbert_worker/Dockerfile.gpu`. The GPU runtime stage copies CUDA 12 + cuDNN 9 libraries from `nvidia/cuda:12.6.3-cudnn-runtime-ubuntu22.04` into the slim Python image (required for ORT's CUDA execution provider).

Requires NVIDIA driver + [Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html).

`.env`:

```env
RERANK_ENABLED=true
COLBERT_EMBED_BACKEND=remote
COLBERT_GPU=1
COLBERT_GPU_COUNT=1
# Optional: pin sidecar to specific GPU(s) when multiple are visible
# COLBERT_DEVICE_IDS=1
```

```bash
docker compose -f docker-compose.yml -f docker-compose.ollama.yml \
  -f docker-compose.colbert-worker.yml \
  -f docker-compose.colbert-worker.gpu.yml up -d --build
```

Verify sidecar device: `curl http://localhost:8082/health` — expect `"device":"cuda"`, `"cuda_available":true`, and `"execution_providers"` containing `CUDAExecutionProvider`. The worker fails at startup if `COLBERT_USE_CUDA=1` but CUDA libraries or the ORT CUDA provider are unavailable, or if the model loads on CPU despite CUDA being requested.

**Single-GPU VRAM:** On an 8 GB GPU, running Ollama dense and ColBERT on the same device may OOM. Prefer a second GPU (`OLLAMA_GPU_COUNT=1` on GPU 0, `COLBERT_DEVICE_IDS=1` on GPU 1) or keep the CPU ColBERT sidecar (`docker-compose.colbert-worker.yml` without the GPU override). There is no automatic GPU scheduler.

| Variable | Default | Role |
|----------|---------|------|
| `COLBERT_GPU` | `0` | Document only — set `1` and add `docker-compose.colbert-worker.gpu.yml` |
| `COLBERT_GPU_COUNT` | `1` | GPUs reserved for ColBERT sidecar when using `.colbert-worker.gpu.yml` |
| `COLBERT_USE_CUDA` | `0` | Worker env — set to `1` automatically by GPU compose override |
| `COLBERT_DEVICE_IDS` | *(empty)* | Optional comma-separated GPU indices passed to fastembed |

#### ColBERT sidecar throughput benchmark

Compare CPU vs GPU sidecar index throughput with the dedicated harness (full pipeline, remote ColBERT, rerank enabled):

```bash
cd mcp_server

# CPU sidecar (default Dockerfile)
uv run python -m benchmarks.bench_colbert_sidecar --output /tmp/cpu-sidecar.json

# GPU sidecar (after starting stack with .colbert-worker.gpu.yml)
uv run python -m benchmarks.bench_colbert_sidecar --output /tmp/gpu-sidecar.json

# Compare results (higher chunks_per_s = better)
uv run python -m benchmarks.bench_colbert_sidecar \
  --compare /tmp/cpu-sidecar.json /tmp/gpu-sidecar.json
```

Requires reachable Qdrant, Ollama, and ColBERT sidecar. Result JSON includes `colbert_sidecar_device` and `colbert_sidecar_cuda_available` from sidecar `/health`.

See [README.md](../README.md) for full env reference and tuning presets.

## Retrieval quality (ANN recall)

After a major re-index or when tuning HNSW parameters (`HNSW_EF`, `HNSW_M`, quantization), verify **approximate nearest neighbor recall** before trusting latency or golden-set metrics:

1. Open the Qdrant Web UI → select the collection → **Check Index Quality** (or use the REST API equivalent).
2. Compare ANN results to exact kNN for a sample of points; low recall suggests raising `hnsw_ef` or reviewing index build settings.
3. Run the golden-set harness ([ADR 0007](adr/0007-ranx-retrieval-evaluation.md)) only after ANN recall looks healthy:

```bash
cd mcp_server
uv sync --extra dev --extra benchmark
uv run python -m benchmarks.eval_retrieval --validate-labels
uv run python -m benchmarks.eval_retrieval --output eval-results.json
uv run python -m benchmarks.suggest_labels "async def run_pipeline"
```

Golden labels use `chunk_id` keys (`sha256("{rel_path}:{start_line}")`). Aliases in `golden_queries.jsonl` are repo-relative (`mcp_server/src/...`); the harness prepends the collection folder to match indexed `rel_path` values. Use **`suggest_labels`** to draft aliases from live search hits. Eval JSON includes **`metrics_by_tag`** for slice-level tuning. See [ADR 0007](adr/0007-ranx-retrieval-evaluation.md#initial-baseline-findings-2026-07-02) for baseline numbers and label pitfalls.

When running the harness on the host (not inside Docker), set `OLLAMA_URL=http://localhost:11434` if `.env` points at `http://ollama:11434`.

## Pipeline output quality (client-side Ragas)

The MCP server is **retrieval-only** ([ADR 0010](adr/0010-defer-ragas-to-client.md), [ADR 0012](adr/0012-retrieval-only-rag-split.md)). End-to-end RAG quality (faithfulness, answer relevancy) is evaluated in the **connected client** where the generator and judge LLM live — not in indexer CI.

### Evaluation split

| Layer | Owner | Tooling |
|-------|-------|---------|
| Retrieval relevance | This repo | `eval_retrieval.py` → `recall@10`, `MRR`, `NDCG@10` |
| Latency | This repo | `bench.py` → p50/p95 |
| Pipeline output | MCP client / integrator | Ragas (or equivalent) on same `query_id`s |

### 2×2 diagnostic

After indexer changes, run retrieval eval, then run your client RAG loop on the same golden set and compare ([Qdrant pipeline eval tutorial](https://qdrant.tech/documentation/improve-search/pipeline-output-quality/)):

| recall@10 (server) | faithfulness (client) | Diagnosis |
|--------------------|------------------------|-----------|
| High | High | Ship |
| High | Low | Generator / prompt problem |
| Low | Low | Fix retrieval first |
| Low | High | Incomplete labels or non-committal answers |

Use a **different model** for generator and judge (tutorial pitfall).

### Shared golden set

`benchmarks/fixtures/golden_queries.jsonl` includes optional **`ground_truth`** reference answers for client-side `context_precision`. Export for Ragas notebooks:

```bash
cd mcp_server
uv run python -m benchmarks.export_ragas_dataset --output ragas-golden.json
uv run python -m benchmarks.export_ragas_dataset --require-ground-truth --output ragas-with-ref.json
```

Client loop (pseudo):

1. For each row: `search_codebase(question=row["question"], collection=row["collection"])`
2. Map hit `content` fields → Ragas `retrieved_contexts`
3. Generate answer with your client LLM → Ragas `response`
4. Score with Ragas using `row.get("ground_truth")` for `context_precision`
5. Join `query_id` with `eval-results.json` `per_query` for the 2×2 table

See [ADR 0010](adr/0010-defer-ragas-to-client.md) for the full contract.

## Observability (Prometheus metrics)

Application metrics are **opt-in** via `METRICS_ENABLED=true` ([ADR 0018](adr/0018-telemetry-observability-otel-prometheus.md) Phase 1). Default deployments are unchanged (`METRICS_ENABLED=false`).

### MCP server

When enabled, the MCP server exposes `GET /metrics` on the same HTTP port as streamable-http (default `127.0.0.1:8000`). Metric names use the `codeindexer_*` prefix (tool latency histograms, index job counters, embed backend error rates, memory pressure events, etc.).

If `MCP_AUTH_TOKEN` is set, `/metrics` follows the same bearer-auth rule as other routes — only `/health` stays unauthenticated. Loopback binding remains the primary guard.

Example scrape config:

```yaml
scrape_configs:
  - job_name: codeindexer-mcp
    static_configs:
      - targets: ["127.0.0.1:8000"]
    metrics_path: /metrics
    # When MCP_AUTH_TOKEN is set:
    # authorization:
    #   credentials: "<token>"
    #   type: Bearer
```

Set in `.env`:

```env
METRICS_ENABLED=true
```

Restart `mcp_server` after env-only changes.

### ColBERT sidecar

When `METRICS_ENABLED=true` in the ColBERT worker container, `GET /metrics` is served on port **8082**. In default Compose, the sidecar is bound to **loopback only** (`127.0.0.1:8082`) — scrape from the host or a co-located Prometheus container; do not expose `/metrics` beyond localhost without auth review.

```yaml
  - job_name: codeindexer-colbert
    static_configs:
      - targets: ["127.0.0.1:8082"]
    metrics_path: /metrics
```

### Qdrant (built-in, no code change)

Qdrant v1.18+ exposes Prometheus metrics at:

```text
http://127.0.0.1:6333/metrics?per_collection=true
```

The `per_collection=true` query parameter adds a `collection` label on REST/gRPC latency metrics — useful for per-project SLOs when each indexed folder is its own collection ([ADR 0004](adr/0004-collection-per-project-isolation.md)). Import [Qdrant's official Grafana dashboard](https://qdrant.tech/documentation/observability/) for storage and query panels.

Optional JSON probes (not Prometheus): `GET /telemetry`, `GET /cluster/telemetry` for shard/optimizer state.

### Traces (Phase 2)

FastMCP already emits OpenTelemetry spans for MCP tool calls when an OTel SDK is configured — see [FastMCP telemetry](https://gofastmcp.com/servers/telemetry). OTLP export and custom domain spans are Phase 2 of ADR 0018; Phase 1 adds **Prometheus metrics only**.

## Fine-tuned embedding model (maintainer / offline)

Production dense inference remains **Ollama-only** ([ADR 0011](adr/0011-ollama-only-dense-embedding.md)). Optional supervised fine-tuning of Qwen3 for this repo’s golden set was **maintainer-run outside Docker** — not part of the default MCP image or CI ([ADR 0020](adr/0020-qwen3-code-finetune-jina-quality-gate.md)). The quality gate **failed** (base Qwen3 recall@10 well below Jina); Phases 2–4 of ADR 0020 are cancelled per [ADR 0021](adr/0021-revert-jina-production-default-retire-qwen3.md).

| Step | Where | Notes |
|------|-------|-------|
| Export golden pairs | `mcp_server/benchmarks/train/export_golden_pairs.py` | Requires indexed Qdrant collection |
| Mine hard negatives | `mcp_server/benchmarks/train/mine_hard_negatives.py` | Uses **base** `qwen3-embedding:4b` hybrid search |
| LoRA train | `mcp_server/benchmarks/train/finetune_qwen3_code.py` | `uv sync --extra train`; CUDA GPU recommended |
| Ollama packaging | Cancelled (ADR 0020 Phase 2) | Gate failed — no promoted checkpoint |
| Quality gate | **Failed** (ADR 0020 Phase 3) | Base Qwen3 did not beat `eval_baseline_jina.json` |

Full workflow: [`mcp_server/benchmarks/train/README.md`](../mcp_server/benchmarks/train/README.md).

**Production default is Jina** — keep `OLLAMA_EMBED_MODEL=unclemusclez/jina-embeddings-v2-base-code` in `.env` ([ADR 0021](adr/0021-revert-jina-production-default-retire-qwen3.md)).

