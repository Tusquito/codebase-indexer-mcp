# 0029. macOS host-native TEI with Metal for dense embedding acceleration

- **Status:** Accepted
- **Date:** 2026-07-12
- **Deciders:** Maintainers
- **Related:** [0028](0028-apple-silicon-arm64-cpu-deployment.md), [0022](0022-gpu-default-cpu-fallback.md), [0025](0025-huggingface-tei-dense-embedding.md), [TEI — Apple Silicon Homebrew](https://github.com/huggingface/text-embeddings-inference#apple-silicon-homebrew), [PyTorch MPS not available in Docker](https://github.com/pytorch/pytorch/issues/81224)
- **Supersedes:** *(partial)* — defers "Apple Metal (future ADR)" footnote in [0022](0022-gpu-default-cpu-fallback.md) § Out of scope

## Context

[ADR 0028](0028-apple-silicon-arm64-cpu-deployment.md) establishes the **baseline** Apple Silicon path: bundled **CPU TEI** in Docker (`cpu-arm64-latest`, `ACCELERATOR=cpu`). That profile is correct and complete but **CPU-bound** for dense embedding — often 5–20× slower than the prior Windows **CUDA TEI** production setup.

Apple Silicon Macs expose a capable GPU via **Metal**, and TEI supports a **host-native** install path:

```shell
brew install text-embeddings-inference
text-embeddings-router --model-id jinaai/jina-embeddings-v2-base-code --port 8080
```

Upstream documents Metal acceleration for this Homebrew binary; **Docker cannot use Metal/MPS** ([PyTorch #81224](https://github.com/pytorch/pytorch/issues/81224), [TEI #611](https://github.com/huggingface/text-embeddings-inference/issues/611)). The bundled `tei` Compose service therefore never achieves GPU-class dense throughput on Mac.

### Problem

| Deployment | Dense embed device | Typical M3 Pro experience |
|------------|-------------------|---------------------------|
| Windows + NVIDIA (prior production) | CUDA TEI sidecar | Fast index; GPU VRAM isolates model RAM |
| [0028](0028-apple-silicon-arm64-cpu-deployment.md) bundled CPU TEI | Docker CPU | Correct but slow; competes with MCP/Qdrant for VM RAM |
| **Host-native Metal TEI (this ADR)** | macOS Metal via Homebrew | Faster dense embed; TEI RAM outside Docker VM |
| [0028](0028-apple-silicon-arm64-cpu-deployment.md) @ 24 GiB Docker VM | Docker CPU, higher caps | Workable baseline on maintainer M3 Pro; Metal still faster for dense |

### Hard constraints

1. **MCP architecture unchanged** — dense stays HTTP TEI ([0025](0025-huggingface-tei-dense-embedding.md)); only `TEI_URL` target moves to the host.
2. **No `ACCELERATOR=metal` in Phase 1** — Metal path is **operator opt-in** via external TEI; [0022](0022-gpu-default-cpu-fallback.md) `gpu`/`cpu` enum unchanged.
3. **No Metal inside MCP or ColBERT containers** — ColBERT remains off or CPU ONNX on Mac ([0028](0028-apple-silicon-arm64-cpu-deployment.md)).
4. **Same model/dim contract** — `DENSE_EMBED_MODEL` and `DENSE_EMBED_VECTOR_SIZE` must match between host TEI and indexed Qdrant data.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Dense embed throughput on Mac | yes | Primary motivation |
| Index/search correctness | yes | Same TEI OpenAI-compatible API |
| Parity with CUDA TEI baseline | partial | Expect closer but not equal to RTX 40xx |
| Golden-set recall@10 | no | Same model → same ranking |
| Bundled Compose `tei` service on Mac | no | Disabled in this profile |

### Why now

- [0028](0028-apple-silicon-arm64-cpu-deployment.md) documents the safe CPU path; maintainers need an **optional faster path** without waiting for hypothetical Docker Metal support.
- TEI Homebrew + Metal is **upstream-supported** for Apple Silicon.
- `docker-compose.yml` already documents **external TEI** (`TEI_URL=http://host.docker.internal:8080`, omit `bundled-tei` profile) — this ADR normative-izes the macOS Metal variant.

## Decision

We will document and test an **optional macOS host-native TEI profile** where:

1. **TEI runs on the macOS host** via Homebrew (`text-embeddings-router`), leveraging **Metal** when available.
2. **MCP + Qdrant (+ cron) run in Docker** on `linux/arm64` without the bundled `tei` service.
3. MCP connects via `TEI_URL=http://host.docker.internal:8080` (Docker Desktop host gateway).

This is **opt-in**, **not** the default Apple Silicon profile ([0028](0028-apple-silicon-arm64-cpu-deployment.md) bundled CPU TEI remains the simpler default).

### Operator profile (normative)

| Variable | Value |
|----------|-------|
| `ACCELERATOR` | `cpu` |
| `COMPOSE_PROFILES` | *(empty — no `bundled-tei`)* |
| `TEI_URL` | `http://host.docker.internal:8080` |
| `DENSE_EMBED_MODEL` | `jinaai/jina-embeddings-v2-base-code` |
| `DENSE_EMBED_VECTOR_SIZE` | `768` |
| `RERANK_ENABLED` | `false` |

**Host TEI (separate terminal or `launchd` plist):**

```bash
brew install text-embeddings-inference
text-embeddings-router \
  --model-id jinaai/jina-embeddings-v2-base-code \
  --hostname 127.0.0.1 \
  --port 8080 \
  --max-batch-tokens 1024
```

Use `--hostname 127.0.0.1` so TEI listens on loopback only (same posture as bundled compose `127.0.0.1:${TEI_PORT}`). Verify the flag against the installed `text-embeddings-router --help` — upstream spelling may differ.

**Docker stack (no TEI container) — maintainer M3 Pro with 24 GiB Docker Desktop VM:**

With host-native TEI, the full 24 GiB Docker budget serves MCP + Qdrant (+ cron) only. Suggested cgroup caps (no bundled `tei`):

```env
ACCELERATOR=cpu
COMPOSE_PROFILES=
TEI_URL=http://host.docker.internal:8080
DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code
DENSE_EMBED_VECTOR_SIZE=768
SPARSE_EMBED_MODEL=Qdrant/bm25
SPARSE_THREADS=2
WORKSPACE_ROOT=/Users/<user>/Documents/Repositories
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

**Unified memory accounting:** host TEI (Jina weights + Metal working set) and the 24 GiB Docker VM **share the same physical RAM**. Do not set Docker Desktop Memory to 24 GiB *and* expect unlimited host TEI headroom — monitor Activity Monitor during first index. If pressure is high, lower `MCP_MEM_LIMIT` or use [0028](0028-apple-silicon-arm64-cpu-deployment.md) bundled CPU TEI instead.

**Docker stack (no TEI container):**

```bash
# compose_files.py with include_tei=False equivalent:
docker compose -f docker-compose.yml up -d --build
docker compose restart mcp_server   # after TEI health OK
curl http://127.0.0.1:8080/health   # host TEI
curl http://localhost:8000/health    # MCP
```

### In scope

| Area | Change |
|------|--------|
| `docs/DEPLOYMENT.md` | § "macOS host-native TEI (Metal)" — Homebrew install, `host.docker.internal`, startup order, 24 GiB Docker VM MCP/Qdrant caps, unified-memory warning |
| `.env.example` | Comment block for external Metal TEI profile (24 GiB Docker VM companion caps) |
| `README.md`, `.github/copilot-instructions.md` | Pointer: faster Mac dense path vs [0028](0028-apple-silicon-arm64-cpu-deployment.md) default |
| `scripts/run_compose_integration.py` | Optional `--external-tei` smoke when `TEI_URL` points at host (skip bundled `tei` service checks; maintainer Mac only) |
| Maintainer checklist | Verify Metal or CPU fallback in TEI logs on first embed (see **Review findings**) |

### Review findings (incorporated)

| Finding | Resolution |
|---------|------------|
| Metal not guaranteed for every model/build | Success criteria require log check on **first real embed**; document that Homebrew TEI may fall back to CPU — uplift vs [0028](0028-apple-silicon-arm64-cpu-deployment.md) bundled path is the gate, not strict Metal |
| Host bind address unspecified | `--hostname 127.0.0.1` in operator profile; confirm flag name in DEPLOYMENT.md from `text-embeddings-router --help` |
| Unified memory pressure with 24 GiB Docker + host TEI | Document shared RAM pool; provide reduced Docker caps table above |
| Integration smoke not CI-runnable | `--external-tei` scoped to maintainer M3 Pro smoke only |
| Maintainer Docker VM is 24 GiB | Docker-side preset uses **MCP 12g + Qdrant 8g** (no TEI cgroup) within 24 GiB VM |

### Out of scope

- **Packaging TEI in MCP image** — remains external HTTP ([0025](0025-huggingface-tei-dense-embedding.md))
- **`brew services` / launchd automation in repo** — document pattern; no committed plist in Phase 1
- **Metal ColBERT** — not supported; rerank stays off on Mac
- **Linux Metal / Vulkan** — macOS only
- **Replacing [0028](0028-apple-silicon-arm64-cpu-deployment.md) default** — bundled CPU TEI stays simpler zero-extra-process path

### Default behavior and configuration

- **Opt-in** — operators choose external TEI when CPU Docker TEI is too slow.
- **Configuration surface:** existing `TEI_URL`, `COMPOSE_PROFILES`; no new Python `Settings` fields.

### Phased delivery

1. **Phase 1 — Documentation** — DEPLOYMENT.md, `.env.example` comments, cross-links from [0028](0028-apple-silicon-arm64-cpu-deployment.md).
2. **Phase 2 — Integration smoke** — `run_compose_integration.py` external-TEI code path; maintainer M3 Pro checklist in ADR Measured outcomes.
3. **Phase 3 — Full-feature benchmark variant** — repeat [0028](0028-apple-silicon-arm64-cpu-deployment.md) Phase 4 tier matrix with host Metal TEI; record in `macos_m3pro_matrix.json` under `metal_host_tei` key.

### Full-feature benchmark variant (Phase 3)

When [0028](0028-apple-silicon-arm64-cpu-deployment.md) tier `full` **OOMs** with bundled CPU TEI inside 24 GiB Docker VM, retest with host Metal TEI — TEI RAM moves out of the VM, leaving more cgroup budget for MCP (ColBERT ONNX) + Qdrant + Neo4j.

**Tier `full` + host Metal TEI preset (24 GiB Docker VM):**

```env
ACCELERATOR=cpu
COMPOSE_PROFILES=
TEI_URL=http://host.docker.internal:8080
DENSE_EMBED_MODEL=jinaai/jina-embeddings-v2-base-code
DENSE_EMBED_VECTOR_SIZE=768
GRAPH_ENABLED=true
NEO4J_PASSWORD=<secure>
NEO4J_MEM_LIMIT=2g
NEO4J_CPUS=2
RERANK_ENABLED=true
COLBERT_EMBED_BACKEND=onnx
UPSERT_BATCH=10
FLUSH_EVERY=96
SEQUENTIAL_EMBED=true
MCP_MEM_LIMIT=8g
QDRANT_MEM_LIMIT=6g
MCP_CPUS=10
QDRANT_CPUS=4
OMP_NUM_THREADS=8
```

Start Homebrew TEI first ([0029](0029-macos-host-native-tei-metal-acceleration.md) § Operator profile), then:

```bash
docker compose $(ACCELERATOR=cpu python scripts/compose_files.py) up -d --build
```

Run the same harness steps B–G from [0028](0028-apple-silicon-arm64-cpu-deployment.md) § Full-feature feasibility benchmark. **Pass bar:** tier `full` completes index + `expand_search_context` + `--rerank` bench without OOM; throughput higher than bundled CPU TEI tier `full` attempt.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Host-native Homebrew TEI + Docker MCP (chosen)** | Metal acceleration; TEI RAM outside VM; reuses external TEI pattern | Two process domains; manual TEI lifecycle; Homebrew version drift |
| Bundled Docker CPU TEI only ([0028](0028-apple-silicon-arm64-cpu-deployment.md)) | Single `compose up`; reproducible | Slow dense embed |
| amd64 TEI container with emulation | One compose command | Slowest; rejected in [0028](0028-apple-silicon-arm64-cpu-deployment.md) |
| `ACCELERATOR=metal` + Compose service | Unified orchestration | No official Metal TEI Docker image; would need custom Dockerfile — high maintenance |
| Ollama dense on Mac | GPU/Metal via Ollama | Retired by [0025](0025-huggingface-tei-dense-embedding.md) |

## Consequences

### Positive

- Restores **practical dense embed throughput** on M-series Macs for day-to-day indexing
- TEI model weights live in **host unified memory**, freeing Docker VM budget for MCP + Qdrant
- No MCP code changes — only `TEI_URL` and compose profile differ
- Closes [0022](0022-gpu-default-cpu-fallback.md) Apple Metal deferral with a shippable operator path

### Negative / trade-offs

- **Operational split** — TEI upgrades via `brew upgrade`; Docker stack via `compose pull`; versions can drift
- **Startup order** — MCP may log `model_preload_failed_continuing` if host TEI starts after MCP; restart MCP once TEI is healthy (existing behavior)
- **Not CI-testable on `ubuntu-latest`** — Mac maintainer smoke only
- **Homebrew TEI feature parity** — must match OpenAI `/v1/embeddings` contract expected by `TeiDenseBackend`
- **Unified memory** — with 24 GiB Docker VM + host TEI, physical RAM is shared; Activity Monitor may show pressure even when cgroup limits are within budget

### Neutral / follow-ups

- Optional `scripts/macos_tei.sh` wrapper for brew install + health wait (Phase 2+)
- Compare Metal vs bundled CPU TEI chunks/sec on M3 Pro in Measured outcomes

### Downstream work

- [0028](0028-apple-silicon-arm64-cpu-deployment.md) — cross-link as default vs accelerated path
- [0024](0024-resource-aware-stack-tuner.md) — external TEI excludes `TEI_MEM_LIMIT` from allocator (already noted for external URL)

## Implementation notes

### New artifacts

| Path | Role |
|------|------|
| `docs/DEPLOYMENT.md` § macOS host-native TEI | Operator guide |

### Modified artifacts

| Path | Change |
|------|--------|
| `.env.example` | External Metal TEI comment block |
| `README.md`, `.github/copilot-instructions.md` | Mac acceleration pointer |
| `scripts/run_compose_integration.py` | Optional external TEI smoke |
| `benchmarks/fixtures/macos_m3pro_matrix.json` | Add `metal_host_tei` tier results (Phase 3) |
| `docs/adr/README.md` | Index row |
| [0022](0022-gpu-default-cpu-fallback.md) | Add cross-link from § Out of scope Apple Silicon bullets *(done in ADR acceptance)* |

### Dependencies

- **Host:** Homebrew, `text-embeddings-inference` formula (Apple Silicon bottle)
- **Docker:** Docker Desktop with `host.docker.internal` gateway
- **Model:** Hugging Face cache on host (`~/.cache/huggingface` or `HF_HOME`)

### Rollout

- Documentation-only Phase 1 — zero production behavior change.

### Data migration

- **None** if same `DENSE_EMBED_MODEL` / `DENSE_EMBED_VECTOR_SIZE` as prior index.
- Switching between bundled CPU TEI and host Metal TEI with same model: **no re-index**.

## Validation

### Automated tests

- **Unit** — existing `TeiDenseBackend` tests mock HTTP; no change required.
- **Integration** — optional maintainer smoke: external `TEI_URL`, skip `tei` compose service assertions.

### Success criteria

1. Host `curl http://127.0.0.1:8080/health` OK; MCP indexes a small repo via `host.docker.internal`.
2. TEI logs on **first embed** show Metal device **or** documented CPU fallback; either must beat [0028](0028-apple-silicon-arm64-cpu-deployment.md) bundled `cpu-arm64-latest` throughput on the same M3 Pro (maintainer benchmark).
3. Dense embed throughput measurably higher than [0028](0028-apple-silicon-arm64-cpu-deployment.md) **24 GiB Docker VM** bundled preset on same machine.
4. `COMPOSE_PROFILES` unset — `codeindexer_tei` container **not** created.
5. TEI listens on `127.0.0.1:8080` only (not `0.0.0.0`).
6. **Phase 3:** tier `full` + host Metal TEI passes on 24 GiB Docker VM when bundled TEI tier `full` fails ([0028](0028-apple-silicon-arm64-cpu-deployment.md) Phase 4).

## Measured outcomes

*(Fill after maintainer M3 Pro benchmark — 24 GiB Docker Desktop VM — delete placeholder when empty.)*

| Variant | Index chunks/s | Notes |
|---------|----------------|-------|
| [0028](0028-apple-silicon-arm64-cpu-deployment.md) bundled `cpu-arm64-latest`, 24 GiB Docker VM | | Baseline |
| [0029](0029-macos-host-native-tei-metal-acceleration.md) Homebrew Metal TEI + 24 GiB Docker MCP/Qdrant | | Target: significant uplift vs baseline |
| [0029](0029-macos-host-native-tei-metal-acceleration.md) tier `full` + host Metal TEI | | Phase 3 — all features enabled |

### Phase 2 maintainer smoke checklist (M3 Pro)

Run **before merge approval** on maintainer Apple Silicon Mac with Docker Desktop (24 GiB VM recommended). Not CI-runnable on `ubuntu-latest`.

1. **Host TEI** — `brew install text-embeddings-inference`; start `text-embeddings-router` with Jina model on `127.0.0.1:8080` (see § Operator profile).
2. **Preflight** — `curl http://127.0.0.1:8080/health` returns OK.
3. **Harness** — from repo root:

   ```bash
   ACCELERATOR=cpu python scripts/run_compose_integration.py --json --external-tei
   ```

4. **JSON verdict** — `verdict: pass`; `external_tei: true`; `host_tei_preflight`, `tei_container_absent`, `tei_embed_smoke`, `pytest_integration` all `pass`.
5. **No bundled TEI** — `docker ps --filter name=codeindexer_tei` empty after deploy.
6. **Metal log check** — on first embed in host TEI stdout, confirm Metal or documented CPU fallback (throughput gate vs bundled [0028](0028-apple-silicon-arm64-cpu-deployment.md) path).
7. **Teardown** — harness tears down by default; use `--keep` only for debugging.

### Operational notes

- Start host TEI **before** or restart MCP after TEI is healthy.
- Pin TEI version after validation: `brew pin text-embeddings-inference` (optional).
- Bind TEI to loopback: `--hostname 127.0.0.1` (verify in `text-embeddings-router --help`).
- **24 GiB Docker Desktop VM:** when using host TEI, allocate the full slider to MCP/Qdrant (suggested 12g + 8g); TEI RAM is outside the VM but inside unified memory.
- **Do not** set `ACCELERATOR=gpu` on Mac — Metal is not CUDA; GPU compose files must not merge.
- If unified memory pressure is high during indexing, reduce Docker VM slider or `MCP_MEM_LIMIT`, or fall back to [0028](0028-apple-silicon-arm64-cpu-deployment.md) bundled CPU TEI.
