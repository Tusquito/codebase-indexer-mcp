# Deployment

Docker Compose runs three services: Qdrant, the MCP server, and the cron reindex job. All configuration is env-var driven via `.env` (copy from `.env.example`).

## Compose files

| File | Purpose |
|------|---------|
| `docker-compose.yml` | Base stack: `codeindexer_qdrant`, `codeindexer_mcp`, `codeindexer_cron` |
| `docker-compose.gpu.yml` | NVIDIA CUDA override — GPU device reservation, `EMBED_DEVICE=cuda` build |
| `docker-compose.amd.yml` | AMD ROCm **native Linux** — `/dev/kfd` + `/dev/dri`, `ROCM_VARIANT=native` |
| `docker-compose.amd.wsl2.yml` | AMD ROCm **Windows + WSL2** — `/dev/dxg`, `ROCM_VARIANT=wsl` |

### CPU (default)

```bash
cp .env.example .env
# Edit WORKSPACE_ROOT and required vars
docker compose up -d --build
```

### NVIDIA CUDA

Prerequisites: NVIDIA driver, [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html), `EMBED_DEVICE=cuda` in `.env`.

```bash
EMBED_DEVICE=cuda docker compose -f docker-compose.yml -f docker-compose.gpu.yml up -d --build
```

Rebuild when switching `cpu` ↔ `cuda` — the Dockerfile selects different base images and `fastembed` vs `fastembed-gpu`.

### AMD ROCm (native Linux)

Prerequisites: ROCm 6.4+ driver, `/dev/kfd` and `/dev/dri` passthrough, `EMBED_DEVICE=rocm` in `.env`.

```bash
EMBED_DEVICE=rocm docker compose -f docker-compose.yml -f docker-compose.amd.yml up -d --build
```

Build arg `ROCM_VARIANT=native` → ROCm 6.4.4 + `onnxruntime-rocm`.

### AMD ROCm (WSL2)

Prerequisites: Adrenalin 26.2.2+ on Windows, ROCm 7.2.1+ in WSL2, `/dev/dxg` present.

```bash
EMBED_DEVICE=rocm docker compose -f docker-compose.yml -f docker-compose.amd.wsl2.yml up -d --build
```

Build arg `ROCM_VARIANT=wsl` → ROCm 7.2.1 + `onnxruntime_migraphx`. Dense embedding uses `ROCMExecutionProvider` on WSL2 (MIGraphX EP unsupported).

## Build matrix summary

| Target | `EMBED_DEVICE` | Compose override | `ROCM_VARIANT` | Dense provider |
|--------|----------------|------------------|----------------|----------------|
| CPU | `cpu` | *(none)* | — | `CPUExecutionProvider` |
| NVIDIA | `cuda` | `docker-compose.gpu.yml` | — | `CUDAExecutionProvider` |
| AMD Linux | `rocm` | `docker-compose.amd.yml` | `native` | MIGraphX / ROCm |
| AMD WSL2 | `rocm` | `docker-compose.amd.wsl2.yml` | `wsl` | `ROCMExecutionProvider` |

Sparse embedding (`SPARSE_EMBED_MODEL`, default BM25) always runs on **CPU**.

## Memory and CPU tuning

Required resource caps (Docker Compose — not read by Python directly):

| Variable | Role |
|----------|------|
| `MCP_MEM_LIMIT` | cgroup memory cap for MCP server |
| `QDRANT_MEM_LIMIT` | cgroup memory cap for Qdrant |
| `MCP_CPUS` | CPU limit for MCP server |
| `QDRANT_CPUS` | CPU limit for Qdrant |
| `OMP_NUM_THREADS` | ONNX/BLAS thread count |

**Rule of thumb:** `MCP_MEM_LIMIT + QDRANT_MEM_LIMIT` must leave 2–3 GiB for kernel, Docker daemon, and WSL2 overhead.

Application knobs (see README Configuration and `.env.example` presets):

| Goal | Suggested changes |
|------|-------------------|
| More RAM | Raise mem limits, `FLUSH_EVERY`, `BATCH_SIZE`; optionally `VECTORS_ON_DISK=false`, `QUANTIZATION=false` |
| More CPU | Raise `OMP_NUM_THREADS` / `DENSE_THREADS`, `BATCH_SIZE`; reserve cores for Qdrant via `QDRANT_CPUS` |
| Smaller host | Lower mem limits, `FLUSH_EVERY`, `BATCH_SIZE`, `OMP_NUM_THREADS`; keep on-disk storage + quantization |
| GPU OOM | Lower `BATCH_SIZE`, `MAX_DENSE_EMBED_TOKENS` — memory-pressure guard tracks **container RAM**, not VRAM |

## Ports and security

Default bindings are loopback-only:

- MCP: `127.0.0.1:8000`
- Qdrant: `127.0.0.1:6333` / `6334`

Set `MCP_AUTH_TOKEN` when exposing beyond localhost.

### Connecting clients

The MCP server publishes streamable HTTP on `127.0.0.1:8000` by default. **Recommended for Cursor** — add to `~/.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "codebase-indexer": {
      "url": "http://localhost:8000/mcp"
    }
  }
}
```

When `MCP_AUTH_TOKEN` is set, include the bearer header (Cursor 3.7.12 uses `"url"` alone — no `type` field):

```json
{
  "mcpServers": {
    "codebase-indexer": {
      "url": "http://localhost:8000/mcp",
      "headers": {
        "Authorization": "Bearer <your-token>"
      }
    }
  }
}
```

URL transport reconnects automatically after `docker compose restart mcp_server` without a manual Cursor reload.

**Fallback (stdio):** when localhost HTTP is blocked or a client requires stdio, uncomment the disabled `proxy` service in `docker-compose.yml` and use `docker exec` into `codeindexer_proxy` (not `codeindexer_mcp`). The sidecar reads `MCP_AUTH_TOKEN` from env and forwards to `http://mcp_server:8000/mcp`. See [README — MCP Client Configuration](../README.md#mcp-client-configuration).

`codeindexer_cron` also reads `MCP_AUTH_TOKEN` for scheduled re-index calls.

## Volumes

- `qdrant_data` — persistent vector storage
- `fastembed_cache` — downloaded ONNX models (enables `HF_HUB_OFFLINE=1` after first run)

## Health checks

```bash
docker compose ps
curl http://localhost:8000/health
docker logs -f codeindexer_mcp
docker logs -f codeindexer_cron
```

## Local development (no Docker)

```bash
cd mcp_server
uv sync --extra dev
# Qdrant must be reachable at QDRANT_URL
uv run python -m codebase_indexer.main
```

See [CONTRIBUTING.md](../CONTRIBUTING.md) for lint, type-check, and test commands.
