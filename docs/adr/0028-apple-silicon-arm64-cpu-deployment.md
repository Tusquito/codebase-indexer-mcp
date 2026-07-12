# 0028. Apple Silicon arm64 CPU-first deployment profile

- **Status:** Accepted
- **Date:** 2026-07-12
- **Deciders:** Maintainers
- **Related:** [0022](0022-gpu-default-cpu-fallback.md), [0025](0025-huggingface-tei-dense-embedding.md), [0024](0024-resource-aware-stack-tuner.md), [0015](0015-colbert-http-sidecar.md), [Text Embeddings Inference — Docker images](https://github.com/huggingface/text-embeddings-inference#docker-images), [Text Embeddings Inference — ARM64](https://github.com/huggingface/text-embeddings-inference#arm64--aarch64)
- **Supersedes:** *(none — complements [0022](0022-gpu-default-cpu-fallback.md) for non-NVIDIA hosts; does not change GPU-default policy on NVIDIA hardware)*

## Context

The stack was developed and benchmarked on **Windows amd64 hosts with NVIDIA GPUs** (`ACCELERATOR=gpu`, CUDA TEI image `89-1.9`, optional ColBERT GPU sidecar). An **Apple Silicon MacBook Pro (M3 Pro)** has:

| Property | M3 Pro laptop | Prior Windows + NVIDIA target |
|----------|---------------|-------------------------------|
| Host ISA | **arm64** (aarch64) | amd64 (x86_64) |
| Discrete NVIDIA GPU | **None** | RTX / datacenter card |
| Apple GPU (Metal) | Unified memory; **not exposed to Docker** | N/A |
| Docker runtime | Docker Desktop Linux VM (`linux/arm64`) | WSL2 / native Linux (`linux/amd64`) |
| Typical RAM | 18 GiB unified (base) or 36 GiB | 16–32 GiB + discrete VRAM |
| Docker Desktop VM RAM | Operator-configured; **maintainer M3 Pro: 24 GiB** | WSL2 / VM slice of host RAM |

[ADR 0022](0022-gpu-default-cpu-fallback.md) already defines the **only** non-NVIDIA path: `ACCELERATOR=cpu` with explicit operator intent. It also defers **Apple Metal** to a future ADR and states that until then Apple hosts use `ACCELERATOR=cpu`. What is **missing** today:

1. **No arm64-aware TEI image default** — `scripts/compose_files.py` maps `ACCELERATOR=cpu` → `cpu-1.9` (x86_64). On Apple Silicon, Docker Desktop may pull or emulate amd64 images under Rosetta/QEMU, yielding **slow cold starts, higher RAM, and MKL/AVX misconfiguration** (`TEI_MKL_INSTRUCTIONS=AVX2` is Intel-specific and irrelevant on arm64 TEI).
2. **No macOS deployment preset** — `.env.example` presets use Windows paths (`C:\Users\me\repos`) and WSL2-oriented reserve guidance (2–3 GiB). Docker Desktop on macOS needs a **larger host reserve** (macOS + VM overhead).
3. **No documented equivalence** — operators migrating from Windows+NVIDIA lack a single profile that answers: native arm64 vs amd64 emulation, which TEI tag, which ColBERT mode, and how to size cgroup caps to the **Docker Desktop VM RAM budget** (maintainer: 24 GiB).
4. **Host detection gaps** — `scripts/tune_alloc.py` detects RAM on Linux (`/proc/meminfo`) and Windows (`ctypes`); **darwin returns `None`**, blocking `tune_stack.py allocate` without manual `--max-ram-gib`.
5. **`TEI_MKL_INSTRUCTIONS` compose mismatch** — `docker-compose.tei.yml` always injects `MKL_ENABLE_INSTRUCTIONS: ${TEI_MKL_INSTRUCTIONS:-AVX2}`; that default is **x86-only** and must not apply to arm64 TEI (operators need an explicit override or compose fix).
6. **`tei_image_default()` not operator-visible** — the helper exists but `compose_files.py` only prints `-f` args; `TEI_IMAGE` must still be set in `.env` until Phase 2 also documents/emits the arch-aware default.

### Measurable gap

| Workload | Windows + NVIDIA (production) | M3 Pro today (undocumented) | Target (this ADR) |
|----------|------------------------------|----------------------------|-------------------|
| Dense embed | GPU TEI `89-1.9` | Fails if `ACCELERATOR=gpu` (no NVIDIA) | **CPU TEI `cpu-arm64-1.9`**, native `linux/arm64` |
| Sparse BM25 | CPU in MCP | Same (arm64 ONNX wheels) | Unchanged |
| ColBERT rerank | GPU sidecar default | GPU sidecar unavailable | **`RERANK_ENABLED=false`** default; optional in-process ONNX only under `ACCELERATOR=cpu` |
| Compose platform | amd64 | Risk of amd64 emulation | **Native arm64** — reject amd64 emulation as default |
| Stack tuner | Works on Linux/Windows | `allocate` fails without `--max-ram-gib` on darwin | darwin RAM detection + macOS reserve default |

### Hard constraints

1. **No NVIDIA on Apple Silicon laptops** — `ACCELERATOR=gpu` must fail fast (existing `require_gpu()` behavior); operators set `ACCELERATOR=cpu`.
2. **No Metal/MPS inside Docker** — PyTorch MPS and TEI Metal builds are **host-native only**; bundled TEI in Compose is CPU-bound on Mac ([TEI README — Apple Silicon disclaimer](https://github.com/huggingface/text-embeddings-inference#apple-silicon-homebrew)). Optional Metal acceleration is [ADR 0029](0029-macos-host-native-tei-metal-acceleration.md).
3. **Same retrieval semantics** — `DENSE_EMBED_MODEL`, `DENSE_EMBED_VECTOR_SIZE`, hybrid RRF, and MCP tools unchanged; only deployment topology and performance differ.
4. **Do not change GPU-default policy globally** — [0022](0022-gpu-default-cpu-fallback.md) remains in effect for NVIDIA hosts; this ADR adds an **Apple Silicon operator profile**, not a new global default.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Stack boots on M3 Pro without NVIDIA | yes | Primary gate |
| Index/search functional correctness | yes | Same models/dims; hybrid search intact |
| Index throughput vs Windows GPU | partial | Expect slower CPU TEI; document expectations, not parity |
| Golden-set recall@10 | no | Quality unchanged by device; no Mac-specific baseline required in Phase 1 |
| Metal-accelerated TEI | no | [ADR 0029](0029-macos-host-native-tei-metal-acceleration.md) |

### Why now

- Maintainer hardware is shifting to Apple Silicon (M3 Pro) without discrete GPU.
- TEI ships a maintained **`cpu-arm64-1.9`** image ([TEI hardware table](https://huggingface.co/docs/text-embeddings-inference/en/supported_models)); the repo still defaults to `cpu-1.9` for all CPU hosts.
- [ADR 0022](0022-gpu-default-cpu-fallback.md) explicitly lists "Apple Metal (future ADR)" — this ADR closes the **documented CPU arm64 path** so orchestration can proceed before optional Metal work.

## Decision

We will adopt a **native arm64 CPU-first deployment profile** for Apple Silicon (M1/M2/M3/M4) Macs as the **recommended replacement** for the prior Windows amd64 + NVIDIA stack. Operators explicitly set `ACCELERATOR=cpu`, use the **arm64 TEI image**, run **native `linux/arm64` containers** (not amd64 emulation), and apply a **macOS-tuned resource preset** sized to the **Docker Desktop VM RAM budget** (not raw host unified memory).

**Maintainer reference host:** M3 Pro with **24 GiB allocated to Docker Desktop** (Settings → Resources → Memory). cgroup `*_MEM_LIMIT` sums must fit inside that VM budget minus ~2 GiB Linux/Docker overhead (~22 GiB usable).

### Operator profile (normative defaults)

| Variable | Apple Silicon value | Rationale |
|----------|---------------------|-----------|
| `ACCELERATOR` | `cpu` | No NVIDIA; only supported non-GPU path ([0022](0022-gpu-default-cpu-fallback.md)) |
| `TEI_IMAGE` | `ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-1.9` | Native aarch64 TEI ([TEI docs](https://huggingface.co/docs/text-embeddings-inference/en/supported_models)) |
| `COMPOSE_PROFILES` | `bundled-tei` | Same dense sidecar topology as production |
| `DENSE_EMBED_MODEL` | `jinaai/jina-embeddings-v2-base-code` | Production default ([0021](0021-revert-jina-production-default-retire-qwen3.md)); quality parity, slower CPU |
| `DENSE_EMBED_VECTOR_SIZE` | `768` | Matches Jina |
| `RERANK_ENABLED` | `false` | ColBERT GPU sidecar unavailable; avoid RAM-heavy multivector path unless headroom allows Phase 3 ONNX path |
| `WORKSPACE_ROOT` | macOS path, e.g. `/Users/<user>/Documents/Repositories` | Host bind mount |
| `TEI_MAX_BATCH_TOKENS` | `1024` | Bound CPU TEI cold start ([0025](0025-huggingface-tei-dense-embedding.md), `docker-compose.tei.yml` comments) |
| `MAX_DENSE_EMBED_TOKENS` | `1024` | Keep ≤ `TEI_MAX_BATCH_TOKENS` |
| `SEQUENTIAL_EMBED` | `false` on 24 GiB Docker VM; `true` on tight 18 GiB hosts | Trade throughput vs peak RSS |
| `TEI_MKL_INSTRUCTIONS` | *(empty on arm64)* | MKL ISA caps are x86-only; set `TEI_MKL_INSTRUCTIONS=` in `.env` until compose omits MKL on arm64 (see **Review findings**) |

**M3 Pro preset — 24 GiB Docker Desktop VM (maintainer reference):**

```env
ACCELERATOR=cpu
COMPOSE_PROFILES=bundled-tei
TEI_IMAGE=ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-1.9
TEI_URL=http://tei:80
DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code
DENSE_EMBED_VECTOR_SIZE=768
SPARSE_EMBED_MODEL=Qdrant/bm25
SPARSE_THREADS=2
WORKSPACE_ROOT=/Users/<user>/Documents/Repositories

# Docker Desktop → Settings → Resources → Memory: 24 GiB (maintainer M3 Pro)
# cgroup sum 20g + ~2g VM overhead fits inside 24g VM
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

`TEI_MEM_LIMIT=8g` matches the CPU TEI headroom used in `run_compose_integration.py` for Jina CPU warmup (4g is too tight and can OOM on first load). With 24 GiB Docker VM, on-disk quantization remains recommended for search RAM but sequential embed is optional.

**M3 Pro preset — 18 GiB Docker Desktop VM (minimal tier):**

```env
# Docker Desktop Memory: ~12–14 GiB; leave 4–6 GiB for macOS on 18 GiB unified host
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

*(Same `ACCELERATOR`, `TEI_IMAGE`, model, and `TEI_MAX_BATCH_TOKENS` vars as the 24 GiB preset.)*

**36 GiB unified Mac hosts:** scale cgroup caps using the `.env.example` 16C/16GB or 32C/64GB ratio blocks, but keep `TEI_IMAGE=cpu-arm64-1.9` and `ACCELERATOR=cpu`; run `tune_stack.py allocate --cpu` after Phase 2 darwin detection lands.

Canonical invocation:

```bash
docker compose $(ACCELERATOR=cpu python scripts/compose_files.py) --profile bundled-tei up -d --build
curl http://localhost:8000/health
curl http://127.0.0.1:8080/health   # TEI ready after model download
```

### Reject amd64 emulation on Apple Silicon

| Option | Verdict |
|--------|---------|
| **Native `linux/arm64` (chosen)** | Fastest path on M-series; TEI `cpu-arm64-1.9`; MCP/Qdrant build arm64 wheels |
| `linux/amd64` via Rosetta/QEMU | **Not recommended** — 2–5× slower TEI inference, higher RAM, wrong MKL knobs; only for debugging upstream x86-only bugs |
| `ACCELERATOR=gpu` hoping for Metal in Docker | **Invalid** — fail fast; Metal requires host-native TEI ([0029](0029-macos-host-native-tei-metal-acceleration.md)) |

Do **not** document `docker compose --platform linux/amd64` as a Mac quick start.

### In scope

| Area | Change |
|------|--------|
| `docs/DEPLOYMENT.md` | New § "Apple Silicon (arm64 CPU)" with 24 GiB Docker VM profile, minimal 18 GiB tier, `cpu-arm64-1.9` |
| `.env.example` | macOS path presets (M3 Pro 24 GiB Docker VM primary; 18 GiB minimal); `TEI_IMAGE=cpu-arm64-1.9` and `TEI_MKL_INSTRUCTIONS=` under CPU section |
| `scripts/compose_files.py` | `TEI_IMAGE_CPU_ARM64_DEFAULT`; `tei_image_default()` branches on host arch; arch detection precedence (below) |
| `docker-compose.tei.yml` *(Phase 2)* | Omit `MKL_ENABLE_INSTRUCTIONS` on arm64 TEI image path, or document `TEI_MKL_INSTRUCTIONS=` override until fixed |
| `scripts/tune_alloc.py` | darwin RAM via `sysctl hw.memsize`; macOS `DEFAULT_RESERVE_GIB=4.0`; budget uses **Docker VM RAM** when detectable |
| `README.md`, `.github/copilot-instructions.md` | Cross-link Apple Silicon profile; note Docker Desktop reserve |
| `scripts/run_compose_integration.py` | Integration env generator uses arch-aware CPU TEI image |
| `benchmarks/fixtures/macos_m3pro_matrix.json` | Phase 4 full-feature tier pass/fail artifact |
| Unit tests | `test_compose_files.py`: arm64 host → `cpu-arm64-1.9`; amd64 → `cpu-1.9` |

### Out of scope

- **Metal/MPS TEI inside Compose** — see [ADR 0029](0029-macos-host-native-tei-metal-acceleration.md)
- **New `ACCELERATOR=metal` global mode** — not extending [0022](0022-gpu-default-cpu-fallback.md) enum in Phase 1
- **ColBERT GPU on Mac** — remains unavailable; enabling rerank on Mac is Phase 3 optional doc only (`COLBERT_EMBED_BACKEND=onnx`, tight `UPSERT_BATCH`, not default)
- **Changing production GPU defaults** — NVIDIA hosts unchanged
- **Linux arm64 servers (Graviton/Ampere)** — same image selection logic applies; Mac-specific reserve/preset prose is the differentiator

### Default behavior and configuration

- **Default for Apple Silicon operators:** opt-in profile — copy M3 Pro preset into `.env`; global repo default remains `ACCELERATOR=gpu` for NVIDIA hosts.
- **Breaking:** none for existing NVIDIA deployments.
- **New env surface:** none required beyond existing `TEI_IMAGE` override; Phase 2 automates `TEI_IMAGE` when unset on arm64.

### Phased delivery

1. **Phase 1 — Documented profile** — DEPLOYMENT.md § Apple Silicon, `.env.example` M3 Pro presets (24 GiB Docker VM primary), README/copilot cross-links; manual `TEI_IMAGE=cpu-arm64-1.9` and `TEI_MKL_INSTRUCTIONS=`.
2. **Phase 2 — Arch-aware compose defaults** — `compose_files.py` arch detection + `tei_image_default()` wired into `run_compose_integration.py`; `tune_alloc.py` darwin detection; MKL compose fix; tests.
3. **Phase 3 — Optional ColBERT on Mac doc** — when `RERANK_ENABLED=true` on Mac: `COLBERT_EMBED_BACKEND=onnx`, `UPSERT_BATCH=10`, `FLUSH_EVERY=96`, warn below 24 GiB Docker VM (no GPU sidecar).
4. **Phase 4 — Full-feature feasibility benchmark** — run the benchmark matrix below on maintainer M3 Pro (24 GiB Docker VM); record pass/fail per feature tier; commit `benchmarks/fixtures/macos_m3pro_matrix.json` when complete.

### Full-feature feasibility benchmark (Phase 4)

Goal: prove which **optional MCP features** can run together on Apple Silicon **without NVIDIA**, within the **24 GiB Docker Desktop VM** (and optionally with [0029](0029-macos-host-native-tei-metal-acceleration.md) host TEI for more VM headroom).

#### Feature tiers (incremental)

| Tier | ID | Enabled features | Compose services |
|------|-----|------------------|------------------|
| 0 | `baseline` | Hybrid search (default), `RECOMMEND_ENABLED=true`, bundled CPU TEI | MCP + Qdrant + TEI + cron |
| 1 | `+rerank` | Tier 0 + `RERANK_ENABLED=true`, `COLBERT_EMBED_BACKEND=onnx` (in-process CPU — no GPU sidecar) | same (no `colbert_worker`) |
| 2 | `+graph` | Tier 0 + `GRAPH_ENABLED=true`, Neo4j, `expand_search_context` tool registered | + Neo4j |
| 3 | `full` | Tier 1 + Tier 2 combined (all optional features on) | MCP + Qdrant + TEI + Neo4j + cron |

**Out of benchmark scope (always off on Mac):** ColBERT GPU sidecar (`ACCELERATOR=gpu`), CUDA TEI.

#### MCP tools exercised per tier

| Tool / path | baseline | +rerank | +graph | full |
|-------------|----------|---------|--------|------|
| `index_codebase` / hybrid index | yes | yes (+ ColBERT multivectors) | yes (+ graph writer) | yes |
| `search_codebase` / `search_symbols` | yes | yes (+ adaptive rerank) | yes | yes |
| `recommend_code` / `find_outlier_chunks` | yes | yes | yes | yes |
| `find_cross_references` (rerank paths) | no | yes | partial | yes |
| `expand_search_context` | no | no | yes | yes |
| `map_service_dependencies` (rerank) | no | yes | yes | yes |

#### 24 GiB Docker VM — bundled TEI presets for benchmark tiers

Usable budget ≈ **22 GiB** (`24 − 2` VM overhead). Tier 3 is **expected tight**; tier 1–2 should pass.

**Tier 0–2 (no ColBERT) — reuse 24 GiB reference preset** from § Operator profile.

**Tier 1 / 3 (`+rerank` / `full`) — add ColBERT in-process knobs:**

```env
RERANK_ENABLED=true
COLBERT_EMBED_BACKEND=onnx
COLBERT_EMBED_MODEL=colbert-ir/colbertv2.0
UPSERT_BATCH=10
FLUSH_EVERY=96
SEQUENTIAL_EMBED=true
# Rebalance cgroup caps for ColBERT RSS in MCP (sum ≤ 20g):
MCP_MEM_LIMIT=6g
QDRANT_MEM_LIMIT=4g
TEI_MEM_LIMIT=6g
```

**Tier 2 / 3 (`+graph` / `full`) — add Neo4j:**

```env
GRAPH_ENABLED=true
NEO4J_PASSWORD=<secure>
NEO4J_MEM_LIMIT=2g
NEO4J_CPUS=2
# Tier 3 full stack with bundled TEI (sum 20g):
MCP_MEM_LIMIT=5g
QDRANT_MEM_LIMIT=4g
TEI_MEM_LIMIT=6g
NEO4J_MEM_LIMIT=2g
NEO4J_CPUS=2
RERANK_ENABLED=true
COLBERT_EMBED_BACKEND=onnx
UPSERT_BATCH=10
FLUSH_EVERY=96
SEQUENTIAL_EMBED=true
```

Canonical compose (full tier):

```bash
docker compose $(ACCELERATOR=cpu python scripts/compose_files.py) \
  --profile bundled-tei up -d --build
# GRAPH_ENABLED=true adds docker-compose.neo4j.yml via compose_files.py
```

#### Benchmark harness (existing tooling)

Run from `mcp_server/` against a live stack. Use a **small real workspace collection** (this repo or one subproject) for tier 0–2; use `benchmarks/bench.py` synthetic corpus for controlled comparison.

| Step | Command | Pass signal |
|------|---------|-------------|
| A. Stack health | `curl localhost:8000/health`; `curl 127.0.0.1:8080/health` | both OK |
| B. Index throughput | `uv run python -m benchmarks.bench --files 300 --output /tmp/tier-N-bench.json` | completes; no `memory_pressure_halt`; `peak_rss_mb` logged |
| C. ColBERT index+search | same with `--rerank` (tiers 1, 3) | index finishes; `search_hybrid_rerank` p95 present in JSON |
| D. Retrieval quality floor | `uv run python -m benchmarks.eval_retrieval --output /tmp/tier-N-eval.json` | recall@10 ≥ 95% of committed baseline ([0007](0007-ranx-retrieval-evaluation.md)) |
| E. Graph tool smoke | MCP `expand_search_context` on indexed collection (tiers 2, 3) | returns `nodes`/`edges` without error |
| F. Recommendation smoke | MCP `recommend_code` with one positive example | non-empty `results` |
| G. Allocator cross-check | `python scripts/tune_stack.py allocate --cpu --colbert --neo4j --max-ram-gib 22` | emitted caps within ±15% of tier 3 preset; sum ≤ 22g |

**Optional Metal path ([0029](0029-macos-host-native-tei-metal-acceleration.md)):** repeat tiers 0–3 with host TEI + higher MCP/Qdrant caps (TEI cgroup removed from VM). See 0029 § Full-feature benchmark variant.

#### Results artifact

Commit after Phase 4 smoke on maintainer hardware:

```
benchmarks/fixtures/macos_m3pro_matrix.json
```

Sketch schema:

```json
{
  "host": "M3 Pro",
  "docker_vm_gib": 24,
  "accelerator": "cpu",
  "tei_image": "cpu-arm64-1.9",
  "tiers": {
    "baseline": { "pass": true, "index_chunks_per_sec": null, "oom": false, "notes": "" },
    "+rerank": { "pass": null, "index_chunks_per_sec": null, "oom": false, "notes": "" },
    "+graph": { "pass": null, "notes": "" },
    "full": { "pass": null, "oom": false, "notes": "expected tight on bundled TEI" }
  }
}
```

#### Full-feature success criteria (Phase 4)

1. **Tier 0 (`baseline`)** — must pass (index + search + recommend tools).
2. **Tier 2 (`+graph`)** — must pass (Neo4j healthy, `expand_search_context` smoke).
3. **Tier 1 (`+rerank`)** — pass if index completes without OOM at tier 1 caps; document throughput vs Windows GPU.
4. **Tier 3 (`full`)** — pass if all tier 1–2 checks pass in one config; if OOM, document **degraded full** (graph+rerank not simultaneous on bundled TEI 24 GiB) and point to 0029 Metal path retest.
5. Quality floor (step D) must not regress below 95% baseline on any **passing** tier.

### Review findings (incorporated)

Quality/feasibility review against the current codebase surfaced these items — all addressed in this ADR or Phase 2 scope:

| Finding | Resolution |
|---------|------------|
| `docker-compose.tei.yml` defaults `MKL_ENABLE_INSTRUCTIONS` to AVX2 | Preset sets `TEI_MKL_INSTRUCTIONS=`; Phase 2 omits MKL env on arm64 or adds compose override |
| `TEI_MEM_LIMIT=4g` too tight for Jina CPU warmup | 24 GiB preset uses `8g` (matches integration harness); minimal tier uses `6g` |
| `tei_image_default()` not emitted at `compose up` | Phase 2 updates `run_compose_integration.py`; operators set `TEI_IMAGE` in `.env` until then |
| Arch detection strategy unspecified | **Precedence:** (1) `docker version --format '{{.Server.Arch}}'` when `docker info` succeeds; (2) fallback `platform.machine()` (`arm64` / `aarch64` → arm64) |
| 36 GiB Mac tier missing | Pointer to scale via `.env.example` tiers + `tune_stack.py allocate --cpu` |
| Maintainer Docker VM is 24 GiB, not 12–14 GiB | **24 GiB preset is normative reference** for orchestration smoke on maintainer M3 Pro |

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Native arm64 + `ACCELERATOR=cpu` + `cpu-arm64-1.9` (chosen)** | Correct ISA; maintained TEI image; same MCP topology | CPU-bound dense embed; slower than Windows GPU |
| Status quo (`cpu-1.9` on Mac) | No code change | Wrong arch risk; emulation; MKL env noise; poor operator experience |
| amd64 emulation (`--platform linux/amd64`) | Runs x86 TEI tag without arch detection | Slow; high RAM; contradicts "appropriate setup" |
| Smaller dense model default on Mac (nomic/bge-small) | Faster CPU indexing | Different vectors → re-index; deviates from production default ([0021](0021-revert-jina-production-default-retire-qwen3.md)) — document as optional dev preset only |
| Linux VM on Mac with passed-through eGPU | Theoretical NVIDIA path | Not available on M3 Pro laptops; out of scope |

## Consequences

### Positive

- Clear migration path from Windows+NVIDIA to M-series Mac without guessing compose flags
- Native arm64 avoids Rosetta emulation tax on TEI (the dominant inference cost)
- Arch-aware `TEI_IMAGE` defaults prevent foot-gun `cpu-1.9` on aarch64
- `tune_stack.py` becomes usable on macOS after darwin RAM detection
- Hybrid search semantics preserved: CPU TEI dense + CPU sparse BM25 in MCP

### Negative / trade-offs

- **Index latency** — CPU TEI on M3 Pro is substantially slower than CUDA TEI on the prior Windows box; acceptable for local dev, not production-throughput parity
- **24 GiB Docker VM is workable** for Jina + Qdrant + MCP + TEI with the reference preset; **18 GiB Docker VM** remains tight — use minimal tier knobs (`SEQUENTIAL_EMBED=true`, lower caps)
- **First TEI start** — model download + CPU warmup can take several minutes; `start_period: 480s` healthcheck already accounts for this
- **ColBERT rerank off by default** on Mac — quality/latency trade-off for RAM; Phase 4 benchmark determines if `full` tier fits 24 GiB bundled TEI

### Neutral / follow-ups

- [ADR 0029](0029-macos-host-native-tei-metal-acceleration.md) — optional Metal TEI on host for faster dense embed
- Refresh maintainer notes when M4/M5 presets differ materially (core count / RAM tiers)
- Optional weekly smoke: `ACCELERATOR=cpu` integration on self-hosted Mac arm64 runner

### Downstream work

- [0029](0029-macos-host-native-tei-metal-acceleration.md) — host-native Metal TEI; Phase 3 full-feature retest if tier `full` OOMs
- [0024](0024-resource-aware-stack-tuner.md) — macOS reserve in allocator tables; `allocate --cpu --colbert --neo4j` for tier 3 caps

## Implementation notes

### New artifacts

| Path | Role |
|------|------|
| `scripts/platform_detect.py` *(optional)* | `host_arch()`, `container_arch()` — shared by compose + tuner; precedence per **Review findings** |
| `mcp_server/tests/test_compose_files_arm64.py` | Arch-aware TEI image selection |
| `benchmarks/fixtures/macos_m3pro_matrix.json` | Phase 4 full-feature tier results (committed after maintainer smoke) |

### Arch detection (Phase 2)

```python
# Precedence for tei_image_default() when ACCELERATOR=cpu and TEI_IMAGE unset:
# 1. docker version --format '{{.Server.Arch}}'  → arm64 | amd64
# 2. platform.machine()                          → arm64/aarch64 | x86_64/amd64
# arm64 → TEI_IMAGE_CPU_ARM64_DEFAULT (cpu-arm64-1.9)
# amd64 → TEI_IMAGE_CPU_DEFAULT (cpu-1.9)
```

`compose_files.py` `main()` continues to print only `-f` args; arch-aware `TEI_IMAGE` is emitted by `run_compose_integration.py` and documented in `.env.example` — not injected into shell env at `compose up` time unless the operator copies the preset.

### Modified artifacts

| Path | Change |
|------|--------|
| `scripts/compose_files.py` | `TEI_IMAGE_CPU_ARM64_DEFAULT`; `tei_image_default()` + `container_arch()` with precedence above |
| `docker-compose.tei.yml` | Phase 2: skip `MKL_ENABLE_INSTRUCTIONS` when `TEI_IMAGE` contains `arm64`, or arm64-specific override file |
| `scripts/tune_alloc.py` | darwin `sysctl hw.memsize`; `DEFAULT_RESERVE_GIB=4.0` on darwin; document using Docker VM RAM (24 GiB maintainer) for `--max-ram-gib` when host RAM ≠ VM budget |
| `docs/DEPLOYMENT.md` | § Apple Silicon deployment; § full-feature benchmark (Phase 4 commands) |
| `.env.example` | M3 Pro 24 GiB Docker VM preset (primary); 18 GiB minimal tier |
| `README.md`, `.github/copilot-instructions.md` | Apple Silicon quick pointer |
| `scripts/run_compose_integration.py` | Arch-aware `TEI_IMAGE` in generated env |
| `benchmarks/fixtures/macos_m3pro_matrix.json` | **Add** — Phase 4 benchmark results (tier pass/fail + metrics) |
| `docs/adr/README.md` | Index rows |

### Dependencies

- **Runtime:** Docker Desktop for Mac (arm64); **maintainer M3 Pro: 24 GiB VM RAM** (Settings → Resources → Memory)
- **Images:** `qdrant/qdrant:v1.18.2` (multi-arch), `python:3.12-slim` (multi-arch), TEI `cpu-arm64-1.9`
- **No new Python packages**

### Rollout

- **Opt-in profile** — no change to CI (`ACCELERATOR=cpu` + `cpu-1.9` on `ubuntu-latest` amd64 remains correct)

### Data migration

- **None** when moving Windows GPU → Mac CPU **if** `DENSE_EMBED_MODEL` and `DENSE_EMBED_VECTOR_SIZE` unchanged (Qdrant volume portable).
- **Full re-index** if switching dense model/dim or moving between emulated amd64 and native arm64 with different TEI builds (unlikely if Jina 768 throughout).

## Validation

### Automated tests

- **Unit** — `tei_image_default(env)` with mocked `container_arch()` → `cpu-arm64-1.9` on arm64, `cpu-1.9` on amd64
- **Unit** — `container_arch()` prefers Docker server arch over `platform.machine()` (mock both paths)
- **Unit** — `detect_host()` on darwin mock → non-`None` `total_ram_gib`
- **Unit** — `compose_file_args(ACCELERATOR=cpu)` never includes `.gpu.yml` on any arch
- **Integration** — manual/self-hosted: M3 Pro `compose up` → `tei` healthy, `index_codebase` smoke on small repo
- **Benchmark (Phase 4)** — maintainer-only: run full-feature tier matrix; commit `macos_m3pro_matrix.json`

### Success criteria

1. Fresh `.env` from **24 GiB Docker VM** M3 Pro preset on Apple Silicon: `docker compose up` succeeds without `ACCELERATOR=gpu` or NVIDIA toolkit
2. `docker inspect codeindexer_tei` reports `Architecture: arm64` (not amd64)
3. `curl http://127.0.0.1:8080/health` returns OK after model load; MCP `/health` OK
4. `python scripts/tune_stack.py analyze` on macOS prints detected RAM without `--max-ram-gib`
5. Documentation does **not** recommend amd64 emulation for Mac quick start
6. Jina CPU TEI warmup completes without OOM at `TEI_MEM_LIMIT=8g` on 24 GiB Docker VM
7. **Phase 4:** tier `baseline` and `+graph` pass; tier `full` pass or documented OOM with 0029 Metal retest path

## Measured outcomes

*(Fill after Phase 2–4 on maintainer M3 Pro — 24 GiB Docker Desktop VM.)*

### Index throughput (tier 0 baseline)

| Variant | Index chunks/s | TEI cold start (s) | Notes |
|---------|----------------|-------------------|-------|
| M3 Pro, 24 GiB Docker VM, native arm64, Jina CPU TEI | | | Phase 2 |
| M3 Pro, 18 GiB Docker VM, minimal preset | | | Comparison |
| Prior Windows, GPU TEI `89-1.9` | *(from 0025 baseline)* | | Reference only |

### Full-feature tier matrix (Phase 4)

| Tier | Pass? | Index chunks/s | OOM / halt? | Notes |
|------|-------|----------------|-------------|-------|
| `baseline` | | | | hybrid + recommend |
| `+rerank` | | | | ColBERT ONNX in MCP |
| `+graph` | | | | Neo4j + `expand_search_context` |
| `full` | | | | all optional features |
| `full` + [0029](0029-macos-host-native-tei-metal-acceleration.md) host TEI | | | | retest if bundled `full` OOMs |

Committed artifact: `benchmarks/fixtures/macos_m3pro_matrix.json`

### Operational notes

- **Docker Desktop → Settings → Resources → Memory:** maintainer M3 Pro uses **24 GiB**. cgroup `MCP + QDRANT + TEI` preset sums to **20 GiB**; leave ~2 GiB for the Linux VM and daemon inside the 24 GiB slice. On 18 GiB unified Macs without 24 GiB Docker allocation, use the minimal tier (~12 GiB VM).
- **Host unified memory ≠ Docker VM budget:** `tune_stack.py --max-ram-gib` should reflect the **Docker Desktop Memory slider**, not necessarily `sysctl hw.memsize` (e.g. 36 GiB Mac with 24 GiB Docker VM → budget 24, not 36).
- **Verify architecture:** `docker version --format '{{.Server.Arch}}'` should show `arm64`; `docker pull --platform linux/arm64 ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-1.9`.
- **MKL on arm64:** set `TEI_MKL_INSTRUCTIONS=` (empty) in `.env` until Phase 2 compose fix; do not use `AVX2` on arm64 TEI.
- For faster local iteration without quality parity, optional dev preset may swap `DENSE_EMBED_MODEL=nomic-ai/nomic-embed-text-v1.5` with full re-index.
- For faster dense embed with same Jina model, see [ADR 0029](0029-macos-host-native-tei-metal-acceleration.md) (host Metal TEI; TEI RAM outside Docker VM).
