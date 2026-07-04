# 0022. GPU-default acceleration; CPU only when explicit

- **Status:** Proposed
- **Date:** 2026-07-04
- **Deciders:** Maintainers
- **Related:** [0011](0011-ollama-only-dense-embedding.md) — Ollama dense + in-process sparse; [0015](0015-colbert-http-sidecar.md) — ColBERT sidecar + GPU worker; [0016](0016-qwen3-embedding-default-dense-model.md) — GPU throughput assumptions; [0021](0021-revert-jina-production-default-retire-qwen3.md) — Jina production default; [0020](0020-qwen3-code-finetune-jina-quality-gate.md) — maintainer GPU training
- **Supersedes:** *(partial)* — GPU-as-optional deployment assumptions in [0015](0015-colbert-http-sidecar.md) (CPU sidecar / in-process ColBERT as default paths). Does **not** supersede [0011](0011-ollama-only-dense-embedding.md) sparse-on-CPU policy.

## Context

Every GPU-capable workload in this stack is **opt-in** today. Operators must set `OLLAMA_GPU=1`, hand-merge `docker-compose.ollama.gpu.yml`, and (for rerank) `COLBERT_GPU=1` plus `docker-compose.colbert-worker.gpu.yml`. Defaults, docs, integration scripts, and CI all assume **CPU** unless the operator discovers the GPU sections.

| Workload | Current default | Target (this ADR) |
|----------|-----------------|-------------------|
| Dense embed (Ollama) | CPU bundled compose | **GPU** always |
| ColBERT rerank | In-process **CPU** ONNX in MCP, or **CPU** sidecar | **GPU remote sidecar** always when rerank on |
| Sparse BM25 (MCP) | In-process **CPU** fastembed ONNX | **CPU always** ([ADR 0011](0011-ollama-only-dense-embedding.md), [0015](0015-colbert-http-sidecar.md)) |
| Golden-set eval / multi-hop eval | CPU Ollama or skipped | **GPU** Ollama |
| Compose integration | CPU-only compose files | **GPU** compose stack |
| Fine-tune pipeline | Ad hoc maintainer GPU | **GPU** required |
| CI (`ubuntu-latest`) | CPU implicit | **`ACCELERATOR=cpu` explicit only** — the sole allowed CPU exception |

There is **no backward-compatibility requirement**. Legacy CPU defaults, CPU compose quick-starts, in-process ColBERT ONNX, and CPU sidecar images are **removed from the default path**, not preserved alongside GPU. Operators who truly need CPU set one explicit flag; everything else assumes NVIDIA is present and fails fast when it is not.

### Why now

- GPU compose overrides, GPU sidecar Dockerfile, and CUDA probes already exist ([ADR 0015](0015-colbert-http-sidecar.md) phase 2).
- [ADR 0021](0021-revert-jina-production-default-retire-qwen3.md) Phase 2 baseline refresh should run on GPU — the only topology we treat as production.
- CPU-default tooling wastes maintainer time and misrepresents expected deploy performance.

### Hard constraints

1. **Explicit CPU only** — the **only** supported CPU path is `ACCELERATOR=cpu` (documented for CI without NVIDIA, air-gapped CPU hosts, and local unit tests that mock backends). No silent CPU fallback when `ACCELERATOR` is unset or `gpu`.
2. **Fail fast on missing GPU** — when `ACCELERATOR=gpu` (default), compose up, worker startup, and maintainer scripts **error with actionable messages** if NVIDIA runtime / CUDA is unavailable. No degrade-to-CPU.
3. **Single-GPU VRAM** — Ollama + ColBERT on one 8 GB card may OOM ([ADR 0015](0015-colbert-http-sidecar.md)); document `OLLAMA_GPU_COUNT` / `COLBERT_DEVICE_IDS` for multi-GPU; ColBERT-on-CPU is **not** a hidden fallback — operator must set `ACCELERATOR=cpu` or split GPUs.
4. **Sparse BM25 stays in-process CPU** — [ADR 0011](0011-ollama-only-dense-embedding.md) and [ADR 0015](0015-colbert-http-sidecar.md): avoid `fastembed-gpu` / dual ORT runtimes in the MCP image. BM25 is **never** moved to GPU under this ADR; `ACCELERATOR=gpu` does not change sparse device.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Index / embed throughput | yes | Primary motivation |
| Golden-set retrieval quality | partial | Same model + dims; baselines captured on GPU only |
| End-user Ragas | no | [ADR 0010](0010-defer-ragas-to-client.md) |

## Decision

We will make **GPU the unconditional default** for every **GPU-capable** inference workload in this repository. **CPU is used only when `ACCELERATOR=cpu` is set explicitly** (or for workloads that are **always CPU** — see below). There is no `auto` mode and no silent fallback.

### Accelerator modes

Single compose-orchestration variable (compose-only, not Python `Settings`):

| `ACCELERATOR` | Behavior |
|---------------|----------|
| `gpu` *(default when unset)* | Always merge GPU compose overrides; always GPU ColBERT sidecar image; always `COLBERT_USE_CUDA=1`; **fail fast** if NVIDIA unavailable |
| `cpu` | Never merge GPU overrides; CPU Ollama; CPU ColBERT sidecar or in-process ONNX only where still supported for this mode; **only** for CI and operator-declared CPU-only hosts |

**Remove** `OLLAMA_GPU=0`, `COLBERT_GPU=0`, and “document only” semantics as defaults. When `ACCELERATOR=gpu`:

| Variable | Value |
|----------|-------|
| `OLLAMA_GPU` | `1` |
| `COLBERT_GPU` | `1` (when ColBERT sidecar active) |
| `COLBERT_USE_CUDA` | `1` |
| `COLBERT_EMBED_BACKEND` | `remote` when `RERANK_ENABLED=true` (in-process CPU ONNX **not** default) |

### Retire CPU-default paths

The following are **removed from defaults** (may remain as code paths reachable only under `ACCELERATOR=cpu`):

| Legacy CPU default | Replacement |
|--------------------|-------------|
| Bundled Ollama without `.ollama.gpu.yml` | GPU override **always** merged unless `ACCELERATOR=cpu` |
| `COLBERT_EMBED_BACKEND=onnx` in-process in MCP | `remote` + GPU sidecar when rerank enabled |
| CPU `colbert_worker/Dockerfile` as sidecar default | `colbert_worker/Dockerfile.gpu` unless `ACCELERATOR=cpu` |
| CPU-only quick start in `DEPLOYMENT.md` / README | GPU quick start; CPU section titled **“Explicit CPU-only (`ACCELERATOR=cpu`)"** |
| `scripts/run_compose_integration.py` CPU compose list | GPU compose list; CI job sets `ACCELERATOR=cpu` |
| Benchmark / eval harness CPU Ollama assumptions | GPU Ollama; params record `accelerator: gpu` |
| `.env.example` `OLLAMA_GPU` commented / `0` | `ACCELERATOR=gpu` uncommented in REQUIRED |
| Nomic “CPU / minimal” preset as default alternative | Nomic optional model on **GPU** Ollama; CPU preset requires `ACCELERATOR=cpu` |

### Sparse BM25 — always CPU

Sparse BM25 (`SPARSE_EMBED_MODEL`, `onnx_sparse.py` in MCP) **remains in-process CPU** for all accelerator modes. This is unchanged from [ADR 0011](0011-ollama-only-dense-embedding.md) and [ADR 0015](0015-colbert-http-sidecar.md):

- Avoids `fastembed-gpu` / `onnxruntime-gpu` dependency lock conflicts in the MCP container
- BM25 ONNX is lightweight; GPU transfer overhead does not justify moving it
- `ACCELERATOR=gpu` accelerates **Ollama dense** and **ColBERT sidecar** only; MCP continues sparse on `CPUExecutionProvider`

No sparse sidecar, no MCP GPU deps for BM25. Docs must state explicitly: **hybrid search = GPU dense + CPU sparse**.

### Compose orchestration

`scripts/compose_files.py` returns the canonical `-f` list. **Default (`ACCELERATOR=gpu`):**

```
docker-compose.yml
docker-compose.ollama.yml
docker-compose.ollama.gpu.yml
docker-compose.colbert-worker.yml      # when RERANK_ENABLED + remote ColBERT
docker-compose.colbert-worker.gpu.yml
```

When `ACCELERATOR=cpu`, omit both `.gpu.yml` files.

Standard invocation (document in README):

```bash
docker compose $(python scripts/compose_files.py) --profile bundled-ollama up -d --build
```

No hand-assembled file lists. No “optional GPU” wording.

### In scope

| Area | Change |
|------|--------|
| `.env.example` | `ACCELERATOR=gpu` in REQUIRED; `OLLAMA_GPU=1`; remove CPU-as-default comments |
| `docs/DEPLOYMENT.md`, `README.md`, `ARCHITECTURE.md` | GPU is the only documented default; CPU is explicit exception |
| `docker-compose*.yml` | Document that GPU overrides are part of default stack |
| `scripts/compose_files.py`, `scripts/accelerator.py` | Canonical compose resolution; `require_gpu()` fail-fast helper |
| `scripts/run_compose_integration.py` | GPU compose by default |
| `colbert_worker` | GPU Dockerfile default; fail-fast on missing CUDA when `ACCELERATOR=gpu` |
| MCP embed factory | Default `COLBERT_EMBED_BACKEND=remote` when rerank on; deprecate in-process ColBERT as default |
| Benchmarks / eval / train | GPU Ollama; `eval_baseline.json` params include `accelerator: gpu` |
| `.github/workflows/ci.yml` | **`ACCELERATOR=cpu` on every job** — only place CPU is allowed without operator action |
| Phase 3 | Non-blocking self-hosted GPU CI job with `ACCELERATOR=gpu` (real stack smoke) |

### Out of scope

- **Moving sparse BM25 to GPU** — MCP stays `onnxruntime` CPU + fastembed CPU for sparse ([ADR 0011](0011-ollama-only-dense-embedding.md), [ADR 0015](0015-colbert-http-sidecar.md))
- AMD ROCm / Apple Metal (future ADR; until then those hosts use `ACCELERATOR=cpu` explicitly)
- Automatic multi-GPU scheduling between Ollama and ColBERT (operator configures device IDs)
- Changing default dense model ([ADR 0021](0021-revert-jina-production-default-retire-qwen3.md))
- Mandatory GPU gate on every PR in shared GitHub Actions (CPU jobs stay explicit `ACCELERATOR=cpu`)

### Default behavior and configuration

- **Default:** **breaking** — all installs and maintainer workflows assume GPU; CPU-only behavior requires `ACCELERATOR=cpu`.
- **No migration path** — we do not document “preserve old CPU behavior”; operators on CPU-only hardware set `ACCELERATOR=cpu` knowingly.

**Production preset (only default):**

| Variable | Value |
|----------|-------|
| `ACCELERATOR` | `gpu` |
| `COMPOSE_PROFILES` | `bundled-ollama` |
| `OLLAMA_GPU` | `1` |
| `OLLAMA_EMBED_MODEL` | `unclemusclez/jina-embeddings-v2-base-code` |
| `DENSE_EMBED_VECTOR_SIZE` | `768` |
| `RERANK_ENABLED` | `false` *(unchanged)*; when `true`, `COLBERT_EMBED_BACKEND=remote` + GPU sidecar |
| `SPARSE_EMBED_MODEL` / `SPARSE_THREADS` | Unchanged — BM25 always CPU in MCP regardless of `ACCELERATOR` |

**Explicit CPU-only exception:**

| Variable | Value |
|----------|-------|
| `ACCELERATOR` | `cpu` |
| `OLLAMA_GPU` | `0` |
| `COLBERT_GPU` | `0` |

Use for: GitHub Actions, air-gapped CPU servers, developer choice. Not documented as production default.

### Phased delivery

1. **Phase 1 — GPU-default compose + docs** — `ACCELERATOR=gpu` default; `compose_files.py`; merge GPU yml by default; `.env.example` / DEPLOYMENT / README; fail-fast probe; integration script on GPU stack.
2. **Phase 2 — Retire CPU ColBERT defaults** — ColBERT remote GPU sidecar default when rerank on; remove in-process ColBERT from default factory path; [ADR 0021](0021-revert-jina-production-default-retire-qwen3.md) Phase 2 baseline on GPU.
3. **Phase 3 — CI split** — all `ubuntu-latest` jobs: `ACCELERATOR=cpu` explicit; optional self-hosted GPU smoke with `ACCELERATOR=gpu`.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **GPU default + explicit `ACCELERATOR=cpu` only (chosen)** | Fast everywhere; one mental model; no legacy CPU paths | Breaks CPU-only hosts without explicit flag; requires NVIDIA for default path |
| **GPU-first with `auto` CPU fallback** | Softer on laptops | Silent slow path; contradicts “no backward compat / CPU only when explicit” |
| **Status quo (opt-in GPU)** | No change | Slow defaults; rejected |
| **Preserve CPU compose as parallel default** | Easier for old installs | Rejected — no backward compatibility requirement |

## Consequences

### Positive

- Every GPU-capable path (Ollama dense, ColBERT sidecar, integration, eval, benchmark) runs at GPU speed by default
- Hybrid topology stays clear: **GPU dense + CPU sparse BM25** in MCP
- No duplicate CPU/GPU documentation or compose recipes
- Fail-fast surfaces missing Container Toolkit immediately
- CI remains green via **explicit** `ACCELERATOR=cpu` only

### Negative / trade-offs

- **Breaking** for CPU-only hosts — must set `ACCELERATOR=cpu` or install NVIDIA stack
- Single-GPU 8 GB OOM still possible — multi-GPU config or explicit CPU mode
- Index pipeline still spends CPU time on sparse BM25 pass (unchanged; acceptable per [ADR 0011](0011-ollama-only-dense-embedding.md))

### Downstream work

- [0021](0021-revert-jina-production-default-retire-qwen3.md) Phase 2 — GPU-only baseline capture
- [0015](0015-colbert-http-sidecar.md) — update sidecar docs: GPU default, CPU exception
- [0011](0011-ollama-only-dense-embedding.md) — unchanged sparse CPU policy; cross-link from DEPLOYMENT

## Implementation notes

### New artifacts

| Path | Purpose |
|------|---------|
| `scripts/compose_files.py` | Canonical `-f` list; `ACCELERATOR=gpu` unless `cpu` |
| `scripts/accelerator.py` | `require_gpu()` fail-fast; used by compose + integration scripts |

### Modified artifacts

| Path | Change |
|------|--------|
| `.env.example` | `ACCELERATOR=gpu` required; drop CPU-default Ollama block |
| `docs/DEPLOYMENT.md`, `README.md` | GPU-only default; CPU under “Explicit exception” |
| `scripts/run_compose_integration.py` | GPU compose files always (unless env `ACCELERATOR=cpu`) |
| `mcp_server/src/.../factory.py` | Remote ColBERT default when rerank enabled |
| `colbert_worker` | GPU image default in compose |
| `.github/workflows/ci.yml` | `ACCELERATOR=cpu` on all jobs |

*(No changes to `onnx_sparse.py` or sparse deps — BM25 remains CPU.)*

### Rollout

- **Default:** breaking — GPU mandatory unless `ACCELERATOR=cpu`
- **Data migration:** none (same models/dims); re-index not required for device change alone

## Validation

### Automated tests

- **Unit** — `test_compose_files.py`: default → GPU files; `ACCELERATOR=cpu` → no `.gpu.yml`
- **Unit** — `require_gpu()` raises when NVIDIA mock unavailable and `ACCELERATOR=gpu`
- **Integration** — `run_compose_integration.py` with GPU: `ollama ps` shows GPU processor
- **CI** — all jobs pass with explicit `ACCELERATOR=cpu`

### Success criteria

1. Fresh `.env` from `.env.example` on NVIDIA host: `docker compose up` uses GPU without extra flags
2. Same env without NVIDIA: **fails** with clear error (not silent CPU)
3. CI with `ACCELERATOR=cpu`: passes as today
4. No doc section presents CPU as the default install path

## Measured outcomes

*(Fill after Phase 2 — index benchmark on same repo, Jina @ 768, GPU vs explicit CPU.)*

| Variant | Index time (s) | Notes |
|---------|----------------|-------|
| `ACCELERATOR=gpu` (default) | | Production path |
| `ACCELERATOR=cpu` (explicit) | | Exception path only |
