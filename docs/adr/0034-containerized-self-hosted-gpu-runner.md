# 0034. Containerized self-hosted GitHub Actions runner for GPU CI

- **Status:** Proposed
- **Date:** 2026-07-21
- **Deciders:** Maintainers (solo personal project)
- **Related:** [0022](0022-gpu-default-cpu-fallback.md) (CI split: `ubuntu-latest` + `ACCELERATOR=cpu` vs non-blocking `gpu-smoke` on `[self-hosted, gpu]`), [0025](0025-huggingface-tei-dense-embedding.md) (bundled TEI GPU), [0015](0015-colbert-http-sidecar.md) (GPU ColBERT sidecar), [0026](0026-full-stack-embedding-quality-benchmark.md) (GPU bake-off / quality), [DEPLOYMENT.md](../DEPLOYMENT.md) § CI

## Context

GitHub-hosted `ubuntu-latest` runners have **no NVIDIA GPU**. Per [ADR 0022](0022-gpu-default-cpu-fallback.md) Phase 3, merge-gating CI stays on CPU (`ACCELERATOR=cpu`), and a non-blocking `gpu-smoke` job already targets `runs-on: [self-hosted, gpu]` — but **no runner is registered**, so that job never exercises the production GPU compose path (tracker test debt: *gpu-smoke first run when self-hosted runner available*).

Maintainer goals for this **solo, personal** repository:

- Run GPU integration (TEI dense, optional ColBERT, compose harness) on a real NVIDIA GPU for honest latency and CUDA behavior.
- Keep the runner and workloads **containerized** (reproducible, disposable, versioned) — not a bare-metal agent with ad-hoc tooling.
- Accept **no concurrency**: one human, one machine, one runner queue is fine.
- Avoid paying for cloud GPU CI or rewriting the stack for CPU-only approximation.

### Hard constraints

1. **Solo / no concurrency** — one self-hosted runner; no HA, autoscaling, or job matrix across GPU hosts.
2. **Containerized runner + Docker-out-of-Docker** — runner process runs in a container; jobs drive the **host Docker daemon** via mounted `/var/run/docker.sock` so `docker compose` GPU overrides work as in local maintainer runs. Full Docker-in-Docker (DinD) with nested NVIDIA runtime is **out**.
3. **Host provides GPU** — NVIDIA driver + NVIDIA Container Toolkit on the host; CUDA does **not** live inside the runner image.
4. **CPU merge gate unchanged** — lint, unit tests, and blocking `compose-integration` remain on `ubuntu-latest` with `ACCELERATOR=cpu`.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Infrastructure correctness | yes | Runner labels, sock mount, GPU compose smoke |
| Embed / rerank throughput | partial | Faster warm runs; not a new quality gate in Phase 1 |
| Retrieval quality baselines | no | Unchanged; [0026](0026-full-stack-embedding-quality-benchmark.md) may use the runner later |
| End-user MCP contract | no | CI ops only |

## Decision

We will operate **one containerized GitHub Actions self-hosted runner** on the maintainer’s GPU workstation. The runner is labeled at least `self-hosted` and `gpu`. Workflow jobs that need NVIDIA (today: `gpu-smoke`) execute on that machine and run the existing compose integration harness against **local Docker** with `ACCELERATOR=gpu`.

Topology:

```
Maintainer PC (NVIDIA driver + nvidia-container-toolkit + Docker Engine)
└── gha-runner container (official/actions-runner image or thin wrapper)
      ├── env: repo URL + runner token (or ephemeral registration)
      ├── volume: /var/run/docker.sock → host Docker
      └── optional volumes: HF / TEI model caches for warm embeds
            └── job steps: docker compose … (tei.gpu, colbert.gpu, …)
```

### In scope

- Documented compose (or equivalent) definition for the runner container + required host prerequisites
- Labels matching `.github/workflows/ci.yml` (`self-hosted`, `gpu`)
- Persistent cache volumes for model weights / TEI data to cut cold-start time
- DEPLOYMENT / operator runbook: register, start, pause, remove runner
- First successful non-blocking `gpu-smoke` run closing ADR 0022 test debt

### Out of scope

- Multiple runners, org-level runner groups, or autoscaling
- Making `gpu-smoke` blocking on every PR (optional later once stable)
- Cloud GPU runners (GitHub larger runners, AWS/GCP spot, etc.)
- DinD / Sysbox nested Docker with GPU
- Changing default product compose topology or `ACCELERATOR` policy ([0022](0022-gpu-default-cpu-fallback.md))
- Apple Silicon / Metal as the self-hosted GPU path ([0029](0029-macos-host-native-tei-metal-acceleration.md) remains separate)

### Default behavior and configuration

- *Default:* runner **opt-in** for the maintainer; workflows already declare `gpu-smoke` — job stays `continue-on-error: true` until explicitly promoted
- *Configuration surface:* GitHub runner registration token (or fine-grained PAT / org policy as GitHub requires); compose env for repo URL, runner name, labels; host Docker GPU runtime; optional cache volume mounts — **no new MCP `Settings` / product env vars**

### Phased delivery

1. **Phase 1 — Runner compose + docs** — Add a maintainer-only compose file (e.g. `docker-compose.gha-runner.yml`) mounting `docker.sock`, documenting NVIDIA Toolkit prerequisites, labels `self-hosted,gpu`, and start/stop/pause. Update `docs/DEPLOYMENT.md` CI section. No change to merge-gating CPU jobs.
2. **Phase 2 — First green `gpu-smoke`** — Register runner, run `gpu-smoke` end-to-end (`scripts/run_compose_integration.py` with `ACCELERATOR=gpu`), fix harness/docs gaps uncovered on Windows/WSL or Linux host. Record cache volume recommendations from observed cold vs warm times.
3. **Phase 3 — Optional hardening** — Persistent model caches as default mounts; optional “runner offline” checklist; decide whether to keep `continue-on-error: true` or promote selected GPU checks on `main` only (solo maintainer call).

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Chosen: containerized runner + host Docker sock (DooD)** | Matches local compose/GPU path; disposable runner; no DinD GPU pain | Runner container can control host Docker (trust boundary = maintainer machine) |
| Status quo (no runner) | Zero ops | `gpu-smoke` never runs; GPU path untested in GHA |
| Bare-metal runner binary on host | Slightly simpler GPU debug | Less reproducible; drifts from “everything containerized” goal |
| Full DinD + NVIDIA | Stronger isolation from host Docker | Fragile GPU nesting; slower; high ops cost for solo use |
| Cloud GPU CI | No home hardware | Cost, cold starts, secrets, overkill for solo personal project |
| CPU-only forever for CI | Simple | Misrepresents production GPU defaults ([0022](0022-gpu-default-cpu-fallback.md)) |

## Consequences

### Positive

- Production-like GPU integration runs from GitHub Actions without cloud GPUs
- Closes open test debt for `gpu-smoke` / ADR 0022 Phase 3
- Containerized runner stays rebuildable; workloads reuse existing compose files
- Solo workflow: pause the runner container when the GPU is needed for interactive work

### Negative / trade-offs

- Maintainer machine must be **online** for GPU jobs to run (push while laptop asleep → queued/skipped until up)
- Runner with `docker.sock` is effectively root on the Docker host — acceptable only because this is a **trusted solo** repo/machine
- Windows host may need WSL2 + Docker Desktop GPU support; document the supported host OS explicitly in Phase 1
- Non-blocking job can stay red unnoticed unless the maintainer watches Actions

### Neutral / follow-ups

- ADR 0026 Phase 4+ bake-off and quality baselines can reuse the same runner labels
- No product runtime change for end users of the MCP image

### Downstream work

- [0022](0022-gpu-default-cpu-fallback.md) — satisfy self-hosted smoke debt
- [0026](0026-full-stack-embedding-quality-benchmark.md) — optional GPU bake-off on same runner
- [DEPLOYMENT.md](../DEPLOYMENT.md) — CI / self-hosted runner section

## Implementation notes

### New artifacts

- `docker-compose.gha-runner.yml` (name final in Phase 1) — runner service, sock mount, optional cache volumes
- Optional `.env.gha-runner.example` — `REPO_URL`, `RUNNER_TOKEN`, `RUNNER_NAME`, `RUNNER_LABELS=self-hosted,gpu`
- DEPLOYMENT subsection: prerequisites, register, `up`/`down`, pause before gaming/training, security note (sock mount)

### Modified artifacts

- `docs/DEPLOYMENT.md` CI table (link runner setup)
- Possibly `.github/workflows/ci.yml` comments only in Phase 1; behavior change deferred to Phase 3 if promoting gates

### Dependencies

- *Runtime (host):* NVIDIA driver, NVIDIA Container Toolkit, Docker Engine (or Docker Desktop with GPU), GitHub Actions runner image
- *Optional:* persistent volumes for Hugging Face / TEI `/data` caches

### Rollout

- opt-in maintainer tooling; no change to default developer `docker compose` product path

### Data migration

- none (CI ops only; no Qdrant schema or re-index requirement)

## Validation

### Automated tests

- *Unit* — none required for runner compose (ops artifact)
- *Integration* — Phase 2: Actions `gpu-smoke` job green once with runner online (`continue-on-error` may still be true)
- *Fixture smoke* — existing `scripts/run_compose_integration.py` GPU path; TEI `nvidia-smi` assertion already described in DEPLOYMENT

### CI adoption

- Keep `gpu-smoke` **non-blocking** until Phase 3 decision
- Do **not** move lint/unit/CPU compose-integration onto the self-hosted runner

### Success criteria

1. Maintainer can `docker compose -f docker-compose.gha-runner.yml up -d` (or documented equivalent), see the runner **Idle** in GitHub repo settings, and tear it down cleanly
2. Pushing a commit (or `workflow_dispatch` if added) runs `gpu-smoke` on `[self-hosted, gpu]` and completes compose GPU integration with TEI on NVIDIA
3. CPU `ubuntu-latest` merge gate remains unchanged and does not require the home runner
4. Docs state clearly: jobs use **local Docker on the maintainer PC**; pause the runner when the machine should not accept CI
