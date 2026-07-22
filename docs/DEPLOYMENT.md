# Deployment

Docker Compose runs the **Aspire/.NET** stack: Qdrant, TEI, ColBERT worker, and `CodebaseIndexer.Host` MCP. **Dense embedding always goes through TEI**; sparse BM25 is ONNX in-process in the MCP/.NET worker path. Scheduled reindex is **in-process** on the Host (`Reindex:*`) — the `cron/` sidecar and Python MCP runtime were removed (ADR 0030 Phases 6–7). Configuration is `.env` compose caps + `Section__Property` / `appsettings.json`.

## Compose files

| File | Purpose |
|------|---------|
| `docker-compose.aspire.yml` | **Production default** — .NET MCP + Qdrant + TEI + ColBERT. Canonical `-f` list: `python scripts/aspire_compose.py`. **Re-index after pull** (`index_all(force=true)`) when index shape / ColBERT / graph flags change (no schema-version env) |
| `docker-compose.aspire.colbert.gpu.yml` | GPU overlay for Aspire ColBERT (`ACCELERATOR=gpu`) |
| `docker-compose.aspire.neo4j.yml` | Optional Neo4j GraphRAG overlay (`Graph__*` on `mcp`) |

```bash
docker compose $(python scripts/aspire_compose.py) up -d --build
# CPU-only:
docker compose $(ACCELERATOR=cpu python scripts/aspire_compose.py --no-gpu-colbert) up -d --build
# GraphRAG:
docker compose $(python scripts/aspire_compose.py --neo4j) up -d --build
```

### Docker Compose env passthrough

Compose reads host `.env` and injects variables into containers explicitly.

| Service | Source | Notes |
|---------|--------|-------|
| `mcp` | `docker-compose.aspire.yml` | .NET: `Embedding__*`, `Colbert__*`, `Reindex__*`, `Qdrant__*`, `Tei__*`, `Workspace__*` |
| `mcp` | `docker-compose.aspire.neo4j.yml` | `Graph__Enabled`, `Graph__Neo4j*`, hop/node caps |
| `colbert` | aspire compose | `Colbert__*`, `Embedding__CachePath`, optional CUDA overlay |
| `qdrant` / `tei` / `neo4j` | compose only | Resource caps and service env |

Flat names (`DENSE_EMBED_MODEL`, `TEI_URL`, `RERANK_ENABLED`, …) remain for harness/compose convenience and map to `Embedding__*` / `Tei__*` / `Embedding__RerankEnabled`.

**Compose-only variables:** `WORKSPACE_ROOT`, `MCP_MEM_LIMIT`, `QDRANT_MEM_LIMIT`, `MCP_CPUS`, `QDRANT_CPUS`, `ACCELERATOR`, `TEI_IMAGE`, `TEI_MKL_INSTRUCTIONS`, `TEI_MEM_LIMIT`, `TEI_CPUS`, `COLBERT_MEM_LIMIT`, `COLBERT_CPUS`, `NEO4J_MEM_LIMIT`, `NEO4J_CPUS`, `ASPIRE_FASTEMBED_CACHE`.

## GPU-default compose ([ADR 0022](adr/0022-gpu-default-cpu-fallback.md))

**Default:** `ACCELERATOR=gpu` (when unset). Aspire compose merges the ColBERT GPU overlay (`docker-compose.aspire.colbert.gpu.yml`) automatically via `scripts/aspire_compose.py` — do not hand-assemble `-f` lists.

Requires NVIDIA driver + [Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html). Fails fast when GPU is required but NVIDIA runtime is unavailable.

`.env` (production preset):

```env
ACCELERATOR=gpu
TEI_URL=http://tei:80
Tei__Url=http://tei:80
DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code
DENSE_EMBED_VECTOR_SIZE=768
```

```bash
docker compose $(python scripts/aspire_compose.py) up -d --build
```

Verify GPU: `docker exec codeindexer_tei nvidia-smi` — CUDA must be visible in the TEI container.

**Hybrid topology:** GPU dense (TEI) + **CPU sparse BM25** in MCP — unchanged hybrid search model. `ACCELERATOR=gpu` does not move sparse embedding to GPU.

### Explicit CPU-only (`ACCELERATOR=cpu`)

The **only** supported CPU path. Use for GitHub Actions, air-gapped CPU servers, and developer choice — not documented as production default.

`.env`:

```env
ACCELERATOR=cpu
TEI_URL=http://tei:80
Tei__Url=http://tei:80
TEI_IMAGE=ghcr.io/huggingface/text-embeddings-inference:cpu-1.9
DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code
DENSE_EMBED_VECTOR_SIZE=768
```

```bash
docker compose $(ACCELERATOR=cpu python scripts/aspire_compose.py --no-gpu-colbert) up -d --build
docker compose restart mcp
```

## Apple Silicon (arm64 CPU)

Recommended deployment profile for **M1/M2/M3/M4 Macs** without discrete NVIDIA GPU. Complements [ADR 0022](adr/0022-gpu-default-cpu-fallback.md) (explicit `ACCELERATOR=cpu` only path) and [ADR 0028](adr/0028-apple-silicon-arm64-cpu-deployment.md). Optional faster dense embed via host-native Metal TEI: [ADR 0029](adr/0029-macos-host-native-tei-metal-acceleration.md).

### Context

| Property | Apple Silicon Mac | Prior Windows + NVIDIA target |
|----------|-------------------|-------------------------------|
| Host ISA | **arm64** (aarch64) | amd64 (x86_64) |
| Discrete NVIDIA GPU | **None** | RTX / datacenter card |
| Apple GPU (Metal) | Unified memory; **not exposed to Docker** | N/A |
| Docker runtime | Docker Desktop Linux VM (`linux/arm64`) | WSL2 / native Linux (`linux/amd64`) |
| Typical RAM | 18 GiB unified (base) or 36 GiB | 16–32 GiB + discrete VRAM |
| Docker Desktop VM RAM | Operator-configured; **maintainer M3 Pro: 24 GiB** | WSL2 / VM slice of host RAM |

**Do not use `ACCELERATOR=gpu` on Apple Silicon** — no NVIDIA runtime; compose fails fast. Metal/MPS acceleration is **host-native only** (not inside bundled Compose TEI).

### Normative operator variables

| Variable | Apple Silicon value | Rationale |
|----------|---------------------|-----------|
| `ACCELERATOR` | `cpu` | Only supported non-GPU path ([0022](adr/0022-gpu-default-cpu-fallback.md)) |
| `TEI_IMAGE` | `ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-latest` | Native aarch64 TEI — set in `.env` or use integration harness on arm64 |
| `DENSE_EMBED_MODEL` | `jinaai/jina-embeddings-v2-base-code` | Production default; quality parity, slower CPU |
| `DENSE_EMBED_VECTOR_SIZE` | `768` | Matches Jina |
| `RERANK_ENABLED` | `false` | ColBERT GPU sidecar unavailable; avoid RAM-heavy multivector path by default |
| `TEI_MKL_INSTRUCTIONS` | *(empty)* | MKL ISA caps are x86-only — omit on arm64 |
| `TEI_MAX_BATCH_TOKENS` | `1024` | Bound CPU TEI cold start |
| `MAX_DENSE_EMBED_TOKENS` | `1024` | Keep ≤ `TEI_MAX_BATCH_TOKENS` |
| `WORKSPACE_ROOT` | macOS path, e.g. `/Users/<user>/Documents/Repositories` | Host bind mount |

### Docker Desktop memory guidance

**Host unified memory ≠ Docker VM budget.** Size cgroup `*_MEM_LIMIT` sums to the **Docker Desktop → Settings → Resources → Memory** slider, not raw `sysctl hw.memsize`.

| Host | Recommended Docker VM | macOS reserve |
|------|----------------------|---------------|
| 18 GiB unified Mac | ~12–14 GiB VM | Leave **4–6 GiB** for macOS + VM overhead |
| M3 Pro maintainer reference | **24 GiB VM** | cgroup sum **20 GiB** + ~2 GiB Linux/Docker inside VM |
| 36 GiB unified Mac | Scale via `.env.example` 16C/16GB or 32C/64GB ratio blocks | Keep `TEI_IMAGE=cpu-arm64-latest` and `ACCELERATOR=cpu` |

On macOS, reserve **at least 4 GiB** for macOS and Docker Desktop VM overhead (vs 2–3 GiB on WSL2). Over-allocating cgroup caps causes silent OOM kills inside the Linux VM.

### M3 Pro preset — 24 GiB Docker Desktop VM (primary)

Maintainer reference host. Copy into `.env` (see also `.env.example` TUNING PRESETS):

```env
ACCELERATOR=cpu
TEI_IMAGE=ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-latest
TEI_URL=http://tei:80
Tei__Url=http://tei:80
DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code
DENSE_EMBED_VECTOR_SIZE=768
SPARSE_EMBED_MODEL=Qdrant/bm25
SPARSE_THREADS=2
WORKSPACE_ROOT=/Users/<user>/Documents/Repositories

# Docker Desktop → Settings → Resources → Memory: 24 GiB
TEI_MKL_INSTRUCTIONS=
MCP_MEM_LIMIT=7g
QDRANT_MEM_LIMIT=5g
TEI_MEM_LIMIT=8g
MCP_CPUS=10
QDRANT_CPUS=4
TEI_CPUS=4
OMP_NUM_THREADS=8

BATCH_SIZE=32
FLUSH_EVERY=1500
TEI_MAX_BATCH_TOKENS=1024
MAX_DENSE_EMBED_TOKENS=1024
SEQUENTIAL_EMBED=false
VECTORS_ON_DISK=true
QUANTIZATION=true
RERANK_ENABLED=false
```

`TEI_MEM_LIMIT=8g` matches integration harness headroom for Jina CPU warmup (`4g` is too tight and can OOM on first load).

### M3 Pro preset — 18 GiB Docker Desktop VM (minimal tier)

For 18 GiB unified Macs without 24 GiB Docker allocation. Same `ACCELERATOR`, `TEI_IMAGE`, model, and `TEI_MAX_BATCH_TOKENS` as above; tighter cgroup caps:

```env
TEI_MKL_INSTRUCTIONS=
MCP_MEM_LIMIT=4g
QDRANT_MEM_LIMIT=2g
TEI_MEM_LIMIT=6g
MCP_CPUS=8
QDRANT_CPUS=2
TEI_CPUS=4
OMP_NUM_THREADS=4
BATCH_SIZE=16
FLUSH_EVERY=750
SEQUENTIAL_EMBED=true
VECTORS_ON_DISK=true
QUANTIZATION=true
RERANK_ENABLED=false
```

### Reject amd64 emulation

| Option | Verdict |
|--------|---------|
| **Native `linux/arm64` (chosen)** | Fastest path on M-series; TEI `cpu-arm64-latest`; MCP/Qdrant build arm64 wheels |
| `linux/amd64` via Rosetta/QEMU | **Not recommended** — 2–5× slower TEI inference, higher RAM, wrong MKL knobs |
| `ACCELERATOR=gpu` hoping for Metal in Docker | **Invalid** — fail fast; Metal requires host-native TEI ([0029](adr/0029-macos-host-native-tei-metal-acceleration.md)) |

Do **not** document `docker compose --platform linux/amd64` as a Mac quick start. Do **not** use `TEI_IMAGE=cpu-1.9` (amd64) on Apple Silicon — risk of emulation and `AVX2` MKL misconfiguration.

### Canonical compose command

```bash
docker compose $(ACCELERATOR=cpu python scripts/aspire_compose.py --no-gpu-colbert) up -d --build
docker compose restart mcp   # if TEI starts after MCP
```

### Health and architecture verification

```bash
# MCP and TEI health (TEI may take several minutes on first Jina download)
curl http://localhost:8000/health
curl http://127.0.0.1:8080/health

# Confirm native arm64 (not amd64 emulation)
docker version --format '{{.Server.Arch}}'    # expect: arm64
docker inspect codeindexer_tei --format '{{.Architecture}}'   # expect: arm64

# Pull native TEI image explicitly if unsure
docker pull --platform linux/arm64 ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-latest
```

MCP logs should show `tei_embed_ready` after TEI warmup. First TEI start can take several minutes; `start_period: 480s` healthcheck accounts for CPU Jina load.

### Stack tuner (darwin)

`scripts/tune_alloc.py` detects host RAM on macOS via `sysctl hw.memsize` and defaults **4 GiB** reserve (`default_reserve_gib()`). Size the budget to your **Docker Desktop Memory** slider when it differs from host unified RAM:

```bash
python scripts/tune_stack.py analyze
python scripts/tune_stack.py allocate --cpu --max-ram-gib 22
```

Pass `--max-ram-gib` matching your **Docker Desktop Memory** slider when host RAM ≠ VM budget (e.g. 36 GiB Mac with 24 GiB Docker VM → `--max-ram-gib 24`).

### Operator checklist

1. Set **Docker Desktop → Resources → Memory** to 24 GiB (or minimal tier ~12–14 GiB on 18 GiB host).
2. Copy M3 Pro preset from `.env.example` into `.env`; set `WORKSPACE_ROOT` to your macOS repos parent path.
3. Set `ACCELERATOR=cpu` and `TEI_IMAGE=ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-latest` in `.env` (or rely on arch-aware defaults from the integration harness).
4. Keep `RERANK_ENABLED=false` unless you have headroom and accept slower indexing (Phase 3 ColBERT ONNX doc).
5. Run canonical compose command above; verify `arm64` architecture and both health endpoints.
6. Index a small project: `index_codebase(path='<folder>', wait=True)` via MCP client.
7. For faster dense embed with same Jina model, see [§ macOS host-native TEI (Metal)](#macos-host-native-tei-metal) ([ADR 0029](adr/0029-macos-host-native-tei-metal-acceleration.md)).

**Qdrant volume portability:** moving Windows GPU → Mac CPU with unchanged `DENSE_EMBED_MODEL` and `DENSE_EMBED_VECTOR_SIZE` requires no re-index. Full re-index if changing dense model/dim.

## macOS host-native TEI (Metal)

Optional **faster dense embed** on Apple Silicon when bundled Docker CPU TEI is too slow ([ADR 0029](adr/0029-macos-host-native-tei-metal-acceleration.md)). TEI runs **on the macOS host** via Homebrew (Metal when available); MCP + Qdrant stay in Docker. This is **opt-in** — the [§ Apple Silicon](#apple-silicon-arm64-cpu) bundled CPU TEI profile remains the simpler default (single `compose up`, no extra process).

**Why host-native:** Docker cannot use Metal/MPS ([PyTorch #81224](https://github.com/pytorch/pytorch/issues/81224)); bundled `tei` in Compose is always CPU-bound on Mac. Homebrew `text-embeddings-inference` is upstream-supported for Apple Silicon Metal acceleration.

### When to use

| Profile | Dense device | Ops complexity | Throughput |
|---------|--------------|----------------|------------|
| [§ Apple Silicon](#apple-silicon-arm64-cpu) bundled CPU TEI | Docker CPU | Low — one compose command | Baseline |
| **This section — host Metal TEI** | macOS Metal (or CPU fallback) | Medium — host TEI + Docker stack | Target: significant uplift vs bundled CPU |

Same `DENSE_EMBED_MODEL` / `DENSE_EMBED_VECTOR_SIZE` as bundled path — **no re-index** when switching profiles.

### Host TEI install and start

Install once via Homebrew ([upstream docs](https://github.com/huggingface/text-embeddings-inference#apple-silicon-homebrew)):

```bash
brew install text-embeddings-inference
```

Start TEI in a **separate terminal** (or via your own `launchd` plist — not shipped in this repo):

```bash
text-embeddings-router \
  --model-id jinaai/jina-embeddings-v2-base-code \
  --hostname 127.0.0.1 \
  --port 8080 \
  --max-batch-tokens 1024
```

Use `--hostname 127.0.0.1` so TEI listens on loopback only (same posture as bundled compose `127.0.0.1:${TEI_PORT}`). Confirm the flag name against `text-embeddings-router --help` — upstream spelling may differ. Model weights cache under `~/.cache/huggingface` (or `HF_HOME`).

### Docker stack (no bundled TEI)

Leave `COMPOSE_PROFILES` unset (unused on Aspire). Omit the bundled `tei` service so MCP reaches host TEI via Docker Desktop's `host.docker.internal` gateway.

**M3 Pro preset — 24 GiB Docker Desktop VM** (maintainer reference; TEI cgroup removed — full VM budget for MCP + Qdrant):

```env
ACCELERATOR=cpu
TEI_URL=http://host.docker.internal:8080
Tei__Url=http://host.docker.internal:8080
DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code
DENSE_EMBED_VECTOR_SIZE=768
SPARSE_EMBED_MODEL=Qdrant/bm25
SPARSE_THREADS=2
WORKSPACE_ROOT=/Users/<user>/Documents/Repositories

# Docker Desktop → Settings → Resources → Memory: 24 GiB
MCP_MEM_LIMIT=12g
QDRANT_MEM_LIMIT=8g
MCP_CPUS=10
QDRANT_CPUS=4
OMP_NUM_THREADS=8

BATCH_SIZE=32
FLUSH_EVERY=1500
MAX_DENSE_EMBED_TOKENS=1024
SEQUENTIAL_EMBED=false
RERANK_ENABLED=false
```

Copy the full block from `.env.example` (TUNING PRESETS → Apple Silicon host Metal TEI).

### Unified memory accounting

Host TEI (Jina weights + Metal working set) and the Docker Desktop VM **share the same physical unified memory** on Apple Silicon. Do **not** set Docker Desktop Memory to 24 GiB and assume unlimited host TEI headroom — monitor **Activity Monitor** during the first index. If memory pressure is high, lower `MCP_MEM_LIMIT`, reduce the Docker VM slider, or fall back to [§ Apple Silicon](#apple-silicon-arm64-cpu) bundled CPU TEI.

### Startup order

1. Start **host TEI first** and wait until healthy.
2. Start Docker stack (omit bundled `tei`):

```bash
docker compose $(ACCELERATOR=cpu python scripts/aspire_compose.py --no-gpu-colbert) up -d --build qdrant colbert mcp
```

3. If MCP started before TEI was ready, restart once TEI responds:

```bash
docker compose restart mcp
```

MCP may log a TEI preload failure when TEI is unreachable at boot — this is expected; restart after TEI is up.

### Health checks

```bash
# Host TEI (loopback)
curl http://127.0.0.1:8080/health

# MCP (Docker)
curl http://localhost:8000/health
```

Confirm no `codeindexer_tei` container exists:

```bash
docker ps --filter name=codeindexer_tei   # expect empty
```

Index a small project via MCP client to verify end-to-end dense embed through `host.docker.internal`.

### Metal log check (first embed)

On the **first real embed** after startup, inspect host TEI stdout/stderr for device selection. Homebrew TEI may report **Metal** acceleration or fall back to **CPU** depending on model/build — either is acceptable if throughput beats bundled `cpu-arm64-latest` on the same machine ([ADR 0029](adr/0029-macos-host-native-tei-metal-acceleration.md) success criteria).

Look for lines mentioning Metal, MPS, or device placement in the terminal running `text-embeddings-router`. Pin a validated version optionally: `brew pin text-embeddings-inference`.

### Operator checklist (Metal path)

Complete the [§ Apple Silicon operator checklist](#operator-checklist) steps 1–2 (Docker Desktop memory, `WORKSPACE_ROOT`), then:

1. `brew install text-embeddings-inference`
2. Start `text-embeddings-router` with Jina model on `127.0.0.1:8080` (command above)
3. Copy Metal preset into `.env` — `TEI_URL` / `Tei__Url=http://host.docker.internal:8080`
4. Set Docker Desktop Memory to **24 GiB**; use `MCP_MEM_LIMIT=12g`, `QDRANT_MEM_LIMIT=8g` (no `TEI_MEM_LIMIT`)
5. `docker compose $(ACCELERATOR=cpu python scripts/aspire_compose.py --no-gpu-colbert) up -d --build qdrant colbert mcp`; restart `mcp` if needed
6. Verify both health endpoints; confirm `codeindexer_tei` container absent
7. Index a small repo; check TEI logs on first embed for Metal or CPU fallback
8. Monitor Activity Monitor for unified memory pressure during first full index

**Integration harness (Metal profile validation):** with host TEI running, from the repository root:

```bash
ACCELERATOR=cpu python scripts/run_compose_integration.py --json --external-tei
```

Expect `verdict: pass`, `tei_container_absent: pass`, and no `codeindexer_tei` container. Maintainer M3 Pro smoke only — not run in GitHub CI.

**Do not** set `ACCELERATOR=gpu` on Mac — Metal is not CUDA. **Do not** enable ColBERT GPU sidecar; keep `RERANK_ENABLED=false` unless following Phase 3 tier `full` preset in [ADR 0029](adr/0029-macos-host-native-tei-metal-acceleration.md).

## External TEI on the host

Run TEI natively or in your own container on `127.0.0.1:8080`. Omit the Aspire `tei` service at `up` time.

`.env`:

```env
TEI_URL=http://host.docker.internal:8080
Tei__Url=http://host.docker.internal:8080
DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code
DENSE_EMBED_VECTOR_SIZE=768
```

```bash
docker compose $(python scripts/aspire_compose.py) up -d --build qdrant colbert mcp
docker compose restart mcp
```

| Variable | Default | Role |
|----------|---------|------|
| `ACCELERATOR` | `gpu` | Compose-only — `gpu` merges Aspire ColBERT GPU overlay; `cpu` is explicit exception only |
| `TEI_URL` / `Tei__Url` | `http://tei:80` (bundled); `http://host.docker.internal:8080` (external) | MCP → TEI base URL; set explicitly in `.env` for your setup |
| `TEI_GPU` | `1` when `ACCELERATOR=gpu` | Document flag for GPU TEI |
| `TEI_GPU_COUNT` | `1` | GPUs reserved for bundled TEI |
| `DENSE_EMBED_MODEL` | *(from `.env`)* | HF repo id — TEI `--model-id` and OpenAI `model` field (must match `DENSE_EMBED_VECTOR_SIZE`) |
| `TEI_EMBED_BATCH_SIZE` | `32` | Texts per TEI `/v1/embeddings` request |
| `TEI_TIMEOUT` | `120` | HTTP timeout (seconds) for TEI calls |
| `TEI_PORT` | `8080` | Host port when bundled TEI publishes to loopback |
| `TEI_MEM_LIMIT` | `4g` | cgroup memory cap for bundled TEI |
| `TEI_CPUS` | `4` | CPU limit for bundled TEI |
| `TEI_IMAGE` | `89-1.9` (GPU) / `cpu-1.9` (amd64 CPU) / `cpu-arm64-latest` (arm64 CPU) | Compose-only TEI Docker tag override; on Apple Silicon use `cpu-arm64-latest` ([§ Apple Silicon](#apple-silicon-arm64-cpu)) |

Verify MCP: `curl http://localhost:8000/health`. If TEI is down at startup, MCP still serves `/health` until TEI is reachable (restart `mcp` after TEI is ready). **Proposed** [ADR 0031](adr/0031-mcp-liveness-vs-readiness.md) splits liveness (`/health`) from dependency readiness (`/ready`).

**Full re-index required** after changing `DENSE_EMBED_MODEL` or `DENSE_EMBED_VECTOR_SIZE`. See [ADR 0025](adr/0025-huggingface-tei-dense-embedding.md).

Sparse embedding (`SPARSE_EMBED_MODEL`, default BM25) always runs on **CPU** inside MCP.

## Memory and CPU tuning

Required resource caps (Docker Compose — not read by the Host directly):

| Variable | Role |
|----------|------|
| `MCP_MEM_LIMIT` / `MCP_CPUS` | MCP container cap |
| `QDRANT_MEM_LIMIT` / `QDRANT_CPUS` | Qdrant container cap |
| `OMP_NUM_THREADS` | ONNX/BLAS threads for sparse BM25 |
| `TEI_MEM_LIMIT` / `TEI_CPUS` | Bundled TEI service caps |

Pipeline knobs (see `.env.example` presets):

| Goal | Knobs |
|------|-------|
| More CPU | Raise `OMP_NUM_THREADS`, `BATCH_SIZE`, `TEI_CPUS`; reserve cores for Qdrant via `QDRANT_CPUS` |
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

The MCP upsert path retries up to **5 times** with exponential backoff on transient failures. If errors persist, lower `UPSERT_BATCH` first.

**Remote ColBERT sidecar** ([ADR 0015](adr/0015-colbert-http-sidecar.md)): offloading inference does **not** shrink upsert payloads — MCP still holds dense + sparse + returned ColBERT multivectors per flush until Qdrant accepts them. Tune `UPSERT_BATCH` the same way.

**Verified preset** (TEI GPU + ColBERT sidecar + rerank, ~16 GB host): see `.env.example` sidecar block — `UPSERT_BATCH=10`, `FLUSH_EVERY=96`, `MCP_MEM_LIMIT=3g`, `COLBERT_MEM_LIMIT=3g`.

See [SEARCH_BEHAVIOR.md](SEARCH_BEHAVIOR.md#optional-colbert-reranking-rerank_enabledtrue) for search-path details.

### ColBERT GPU sidecar (default when rerank on)

When `RERANK_ENABLED=true` / `Embedding__RerankEnabled=true`, `COLBERT_EMBED_BACKEND` defaults to **`remote`** and `aspire_compose.py` includes the ColBERT service (plus `docker-compose.aspire.colbert.gpu.yml` when `ACCELERATOR=gpu`) ([ADR 0022](adr/0022-gpu-default-cpu-fallback.md) phase 2). The MCP container stays on CPU ONNX for sparse BM25 only.

For **explicit CPU-only** hosts (`ACCELERATOR=cpu`), set `COLBERT_EMBED_BACKEND=onnx` / `Colbert__Backend=onnx` for in-process ColBERT in MCP, or keep `remote` with the CPU ColBERT image (`--no-gpu-colbert`). See [ADR 0015](adr/0015-colbert-http-sidecar.md) (superseded default policy: [ADR 0022](adr/0022-gpu-default-cpu-fallback.md)).

Requires NVIDIA driver + [Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html).

`.env`:

```env
RERANK_ENABLED=true
Embedding__RerankEnabled=true
# COLBERT_EMBED_BACKEND=remote  # default when rerank on
COLBERT_GPU=1
COLBERT_GPU_COUNT=1
# Optional: pin sidecar to specific GPU(s) when multiple are visible
# COLBERT_DEVICE_IDS=1
```

```bash
docker compose $(python scripts/aspire_compose.py) up -d --build
```

Verify sidecar device from **inside the Docker network** (default compose does **not** publish `8082` on the host):

```bash
docker exec codeindexer_colbert curl -s http://127.0.0.1:8082/health
```

Expect CUDA available when the GPU overlay is merged. To scrape from the host, add an explicit loopback publish (e.g. `127.0.0.1:8082:8082`) — do not expose beyond localhost without auth review.

**Single-GPU VRAM:** On an 8 GB GPU, running TEI dense and ColBERT on the same device may OOM. Prefer a second GPU (`TEI_GPU_COUNT=1` on GPU 0, `COLBERT_DEVICE_IDS=1` on GPU 1), lower ColBERT’s ORT arena via `COLBERT_GPU_MEM_LIMIT_BYTES` (default 2 GiB), or set `ACCELERATOR=cpu` for CPU-only ColBERT. There is no automatic GPU scheduler.

| Variable | Default | Role |
|----------|---------|------|
| `COLBERT_GPU` | `1` when `ACCELERATOR=gpu` + remote sidecar | Document flag — GPU overlay merged by `aspire_compose.py` |
| `COLBERT_GPU_COUNT` | `1` | GPUs reserved for ColBERT when using `docker-compose.aspire.colbert.gpu.yml` |
| `COLBERT_USE_CUDA` | `0` | Worker env — set to `1` automatically by GPU compose overlay |
| `COLBERT_DEVICE_IDS` | *(empty)* | Optional comma-separated GPU indices (`Colbert__DeviceIds`) |
| `COLBERT_GPU_MEM_LIMIT_BYTES` | `2147483648` (2 GiB) | ORT CUDA `gpu_mem_limit` (`Colbert__GpuMemLimitBytes`); `0` = uncapped |

#### ColBERT sidecar throughput benchmark

Compare CPU vs GPU sidecar index throughput with the dedicated harness under `benchmarks/` (full pipeline, remote ColBERT, rerank enabled):

```bash
cd benchmarks

# CPU sidecar
uv run python -m benchmarks.bench_colbert_sidecar --output /tmp/cpu-sidecar.json

# GPU sidecar (after starting Aspire stack with ACCELERATOR=gpu)
uv run python -m benchmarks.bench_colbert_sidecar --output /tmp/gpu-sidecar.json

# Compare results (higher chunks_per_s = better)
uv run python -m benchmarks.bench_colbert_sidecar \
  --compare /tmp/cpu-sidecar.json /tmp/gpu-sidecar.json
```

Requires reachable Qdrant, TEI, and ColBERT sidecar. Result JSON includes `colbert_sidecar_device` and `colbert_sidecar_cuda_available` from sidecar `/health`.

See [README.md](../README.md) for full env reference and tuning presets.

## Optional GraphRAG (Neo4j overlay)

Index-time code graph alongside Qdrant ([ADR 0002](adr/0002-graphrag-neo4j-qdrant.md), [ADR 0023](adr/0023-neo4j-primary-call-site-lookup.md), [ADR 0030](adr/0030-migrate-mcp-server-to-dotnet10.md) Phase 5). **Disabled by default** — omit the overlay and leave graph off for the standard stack.

There is **no** `GRAPH_SCHEMA_VERSION` / `Graph:SchemaVersion` env. After pull (or when enabling graph / changing the writer ontology), **re-index** collections (`index_codebase` / `index_all` with `force=true`).

### Aspire / .NET stack (preferred)

```env
GRAPH_ENABLED=true
NEO4J_PASSWORD=your-secure-password
NEO4J_MEM_LIMIT=2g
```

```bash
docker compose $(python scripts/aspire_compose.py --neo4j) up -d --build
# harness:
python scripts/run_compose_integration.py --json --aspire-stack --graph
```

| Variable | .NET config | Default (overlay) | Role |
|----------|-------------|-------------------|------|
| `GRAPH_ENABLED` | `Graph:Enabled` | `true` when aspire neo4j overlay merged | Enable writer + `expand_search_context` + Neo4j Path D |
| `NEO4J_URI` | `Graph:Neo4jUri` | `bolt://neo4j:7687` | Bolt URI |
| `NEO4J_USER` | `Graph:Neo4jUser` | `neo4j` | Auth user |
| `NEO4J_PASSWORD` | `Graph:Neo4jPassword` | *(required)* | Compose fails fast if missing |
| `NEO4J_DATABASE` | `Graph:Neo4jDatabase` | `neo4j` | DB name |
| `GRAPH_WRITER_BATCH` | `Graph:WriterBatch` | `500` | Cypher UNWIND batch |
| `GRAPH_MAX_HOPS` / `GRAPH_MAX_NODES` | `Graph:MaxHops` / `Graph:MaxNodes` | `2` / `200` | `expand_search_context` caps |

Host logs `graph_enabled` / `graph_disabled` at startup (harness signal). With graph on, indexing omits Qdrant `callees`, stamps `graph_call_sites` + `graph_enabled` collection metadata, and writes Neo4j; `find_cross_references` Path D uses Neo4j for graph-ready collections (Qdrant fallback otherwise).

See [ARCHITECTURE.md](ARCHITECTURE.md#graphrag-optional-phase-2-shipped) and [SEARCH_BEHAVIOR.md](SEARCH_BEHAVIOR.md) for ontology and Path D routing.

## Retrieval quality (ANN recall)

After a major re-index or when tuning HNSW parameters (`HNSW_EF`, `HNSW_M`, quantization), verify **approximate nearest neighbor recall** before trusting latency or golden-set metrics:

1. Open the Qdrant Web UI → select the collection → **Check Index Quality** (or use the REST API equivalent).
2. Compare ANN results to exact kNN for a sample of points; low recall suggests raising `hnsw_ef` or reviewing index build settings.
3. Run the golden-set harness ([ADR 0007](adr/0007-ranx-retrieval-evaluation.md)) only after ANN recall looks healthy:

```bash
cd benchmarks
uv sync --extra dev --extra benchmark
uv run python -m benchmarks.eval_retrieval --validate-labels
uv run python -m benchmarks.eval_retrieval --mcp-url http://127.0.0.1:8000/mcp --output eval-results.json
uv run python -m benchmarks.suggest_labels "async def run_pipeline"
```

Golden labels use `chunk_id` keys (`sha256("{rel_path}:{start_line}")`). Aliases in `golden_queries.jsonl` are repo-relative (`src/...`); the harness prepends the collection folder to match indexed `rel_path` values. Use **`suggest_labels`** to draft aliases from live search hits. Eval JSON includes **`metrics_by_tag`** for slice-level tuning. See [ADR 0007](adr/0007-ranx-retrieval-evaluation.md#initial-baseline-findings-2026-07-02) for baseline numbers and label pitfalls.

When running the harness on the host (not inside Docker), set `TEI_URL=http://localhost:8080` if `.env` points at `http://tei:80`.

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
cd benchmarks
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

Restart affected services after env-only changes (`docker compose restart mcp` and, when using the ColBERT sidecar, `colbert`).

### ColBERT sidecar

When `METRICS_ENABLED=true` in the ColBERT worker container, `GET /metrics` is served on port **8082** **inside** that container. Default Compose does **not** publish `8082` to the host — scrape via `docker exec`, a co-located Prometheus on the compose network, or an explicit loopback port mapping you add yourself. Do not expose `/metrics` beyond localhost without auth review.

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

OpenTelemetry spans for MCP tool calls when an OTel SDK is configured are Phase 2 of ADR 0018; Phase 1 adds **Prometheus metrics only**.

## Fine-tuned embedding model (maintainer / offline)

Production dense inference remains **TEI-only** ([ADR 0025](adr/0025-huggingface-tei-dense-embedding.md)). Optional supervised fine-tuning of Qwen3 for this repo’s golden set was **maintainer-run outside Docker** — not part of the default MCP image or CI ([ADR 0020](adr/0020-qwen3-code-finetune-jina-quality-gate.md)). The quality gate **failed** (base Qwen3 recall@10 well below Jina); Phases 2–4 of ADR 0020 are cancelled per [ADR 0021](adr/0021-revert-jina-production-default-retire-qwen3.md).

| Step | Where | Notes |
|------|-------|-------|
| Export golden pairs | `benchmarks/benchmarks/train/export_golden_pairs.py` | Requires indexed Qdrant collection; train scripts deferred pending MCP-HTTP port |
| Mine hard negatives | `benchmarks/benchmarks/train/mine_hard_negatives.py` | Uses **base** `qwen3-embedding:4b` hybrid search |
| LoRA train | `benchmarks/benchmarks/train/finetune_qwen3_code.py` | `uv sync --extra train`; CUDA GPU recommended |
| TEI packaging | Cancelled (ADR 0020 Phase 2) | Gate failed — no promoted checkpoint |
| Quality gate | **Failed** (ADR 0020 Phase 3) | Base Qwen3 did not beat `eval_baseline_jina.json` |

Full workflow: [`benchmarks/benchmarks/train/README.md`](../benchmarks/benchmarks/train/README.md).

**Production default is Jina** — keep `DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code` in `.env` ([ADR 0021](adr/0021-revert-jina-production-default-retire-qwen3.md)).

## Continuous integration ([ADR 0022](adr/0022-gpu-default-cpu-fallback.md) Phase 3)

GitHub Actions (`.github/workflows/ci.yml`) is the **sole supported CPU exception** for this repository: every `ubuntu-latest` job sets `ACCELERATOR=cpu` explicitly. Production defaults assume GPU; CI never relies on silent CPU fallback.

| Job | Runner | `ACCELERATOR` | Gates merge? |
|-----|--------|---------------|--------------|
| `test` | `ubuntu-latest` | `cpu` | yes |
| `compose-integration` | `ubuntu-latest` | `cpu` | no (`continue-on-error`) — full Docker Compose stack via `scripts/run_compose_integration.py --json` (45 min timeout; typically 15min+) |
| `benchmark` | `ubuntu-latest` | `cpu` | no (`continue-on-error`) |
| `eval-retrieval` | `ubuntu-latest` | `cpu` | no |
| `docker-image` | `ubuntu-latest` | `cpu` | no |
| `colbert-gpu-image` | `ubuntu-latest` | `cpu` | no |
| `gpu-smoke` | `[self-hosted, gpu]` | `gpu` | no — real GPU stack smoke; `docker exec codeindexer_tei nvidia-smi` GPU assertion when runner available |

**Compose integration is optional in GitHub CI** (`continue-on-error`) — the full Compose deploy adds 15min+ to every PR, so it surfaces failures without blocking merges. It **is mandatory in the local ADR pipeline**: `adr-integration-tester` runs this same harness before code review on every phase ([`CONTRIBUTING.md`](../CONTRIBUTING.md#docker-compose-integration-adr-pipeline)). The job runs the same harness as local pre-PR validation on the CPU stack (`ACCELERATOR=cpu`): deploy Qdrant + bundled TEI + MCP, health checks, and `tests/test_storage_integration.py`. The GPU processor check is skipped in CPU mode.

**Optional GPU smoke** on a self-hosted NVIDIA runner exercises the production path (`ACCELERATOR=gpu`): the harness pulls Jina, runs a probe embed, and asserts `docker exec codeindexer_tei nvidia-smi` lists the GPU and the running TEI process. Failures do not block merges.

Local maintainer validation before review:

```bash
# CPU path (matches GHA compose-integration)
ACCELERATOR=cpu python scripts/run_compose_integration.py --json

# GPU path (matches gpu-smoke when NVIDIA + Container Toolkit present)
ACCELERATOR=gpu python scripts/run_compose_integration.py --json
```

