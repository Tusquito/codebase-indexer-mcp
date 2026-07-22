# Codebase Indexer MCP — Copilot Instructions

## What this project is

A fully self-hosted MCP server that indexes codebases into a local Qdrant vector database using **TEI for dense embeddings** and in-process BM25 sparse search, then exposes semantic search tools to AI agents. TEI must be reachable for indexing and search.

## Running the server

```bash
# Start Aspire/.NET stack (production default — ADR 0030 Phase 7)
docker compose $(python scripts/aspire_compose.py) up -d --build

# Explicit CPU-only (CI / no NVIDIA)
docker compose $(ACCELERATOR=cpu python scripts/aspire_compose.py --no-gpu-colbert) up -d --build

# Apple Silicon (M1/M2/M3/M4) — native arm64 CPU profile; see docs/DEPLOYMENT.md § Apple Silicon
# .env: ACCELERATOR=cpu, TEI_IMAGE=ghcr.io/huggingface/text-embeddings-inference:cpu-arm64-latest,
#      TEI_MKL_INSTRUCTIONS= (empty), Embedding__RerankEnabled=false; Docker Desktop Memory 24 GiB recommended
docker compose $(ACCELERATOR=cpu python scripts/aspire_compose.py --no-gpu-colbert) up -d --build

# Apple Silicon — optional host Metal TEI (faster dense; ADR 0029; opt-in)
# brew install text-embeddings-inference && text-embeddings-router --model-id jinaai/jina-embeddings-v2-base-code \
#   --hostname 127.0.0.1 --port 8080 --max-batch-tokens 1024
# .env: TEI_URL / Tei__Url=http://host.docker.internal:8080, ACCELERATOR=cpu
# Start host TEI before Docker; omit bundled tei service
# docker compose $(ACCELERATOR=cpu python scripts/aspire_compose.py --no-gpu-colbert) up -d --build qdrant colbert mcp
# See docs/DEPLOYMENT.md § macOS host-native TEI (Metal)

# Check health
curl http://localhost:8000/health

# View MCP server logs
docker logs -f codeindexer_mcp_dotnet
```

All config is env-var driven via `.env` (copy from `.env.example`) plus Aspire `Section__Property` overrides. **Production runtime is .NET** (`src/CodebaseIndexer.Host`, `docker-compose.aspire.yml`). Config sections live in `appsettings.json` (`Qdrant`, `Tei`, `Embedding`, `Workspace`, `Chunking`, `Indexing`, `Colbert`, `Graph`, `Reindex`, `Discovery`); override with `Section__Property` (e.g. `Qdrant__Url`, `Embedding__DenseModel`). Compose-only caps: `WORKSPACE_ROOT`, `MCP_MEM_LIMIT`, `QDRANT_MEM_LIMIT`, `MCP_CPUS`, `QDRANT_CPUS`, `ACCELERATOR`, `TEI_IMAGE`, `ASPIRE_FASTEMBED_CACHE`. Flat Python-style names (`DENSE_EMBED_MODEL`, `TEI_URL`, …) remain for compose/harness convenience and map to `Embedding__*` / `Tei__*`. See [ADR 0030](docs/adr/0030-migrate-mcp-server-to-dotnet10.md), [ADR 0025](docs/adr/0025-huggingface-tei-dense-embedding.md).

**Important:** `MCP_MEM_LIMIT + QDRANT_MEM_LIMIT` must leave headroom for the Linux VM and Docker daemon. **Windows/WSL:** reserve at least 2–3 GiB. **macOS Docker Desktop:** reserve **4–6 GiB** for macOS + VM overhead — size cgroup caps to the Docker Desktop Memory slider, not host unified RAM. Over-allocating causes silent OOM kills. Apple Silicon presets: [DEPLOYMENT.md § Apple Silicon](docs/DEPLOYMENT.md#apple-silicon-arm64-cpu), [ADR 0028](docs/adr/0028-apple-silicon-arm64-cpu-deployment.md), optional Metal TEI [ADR 0029](docs/adr/0029-macos-host-native-tei-metal-acceleration.md).

## Development

**Runtime:** .NET 10 (`CodebaseIndexer.slnx`, `src/`, `test/`). Run `dotnet test CodebaseIndexer.slnx` or `dotnet run --project src/CodebaseIndexer.AppHost`. **C# file layout:** one type per file. **XML docs:** public types/members require `///` (`GenerateDocumentationFile`; CS1591 is an error). **Embedders:** `AddKeyedSingleton` + `EmbedderBackendKeys`. **Re-index after pull** when index shape / ColBERT / graph flags change (no `*_SCHEMA_VERSION` env).

**Python (dev tooling only):** golden-set eval / label helpers under `benchmarks/` (not MCP runtime). Tracker render: `python scripts/render_adr_tracker.py --check`.

```bash
dotnet test CodebaseIndexer.slnx

cd benchmarks
uv sync --extra dev --extra benchmark
uv run pytest -q
uv run python -m benchmarks.eval_retrieval --validate-labels
```

## Architecture

Aspire stack services (`docker-compose.aspire.yml`):
- **Qdrant** (`codeindexer_qdrant`, port 6333/6334) — vector database
- **TEI** (`codeindexer_tei`, port 8080) — dense embeddings
- **ColBERT worker** (`codeindexer_colbert`, port 8082) — late-interaction / rerank
- **MCP host** (`codeindexer_mcp_dotnet`, port 8000) — .NET `CodebaseIndexer.Host`
- **Proxy** (optional) — `src/CodebaseIndexer.Proxy` stdio forwarder
- Scheduled reindex is in-process on the Host (`Reindex:*`); cron sidecar removed (Phase 6)

`WORKSPACE_ROOT` on the host is mounted read-only into the container at `/workspace`. Each direct subdirectory of `/workspace` is one **collection** (indexed project). The collection name is always the folder basename.

### Indexing pipeline (.NET)

Scan → Tree-sitter / sliding-window chunk → TEI dense + ONNX BM25 sparse embed → Qdrant upsert. Chunk IDs: `ChunkId.FromPathAndLine` = SHA-256 of `{rel_path}:{start_line}` (parity with former Python helper).

### MCP tools (Host `Tools/`)

| Tool | Host type | Description |
|------|-----------|-------------|
| `index_codebase` / `index_status` / `stop_indexing` / `index_all` | `IndexTools` | Index jobs |
| `get_collection_summary` | `SummaryTools` | Orientation + build deps |
| `search_symbols` | `SearchTools` | Metadata-only hybrid search |
| `get_file_outline` | `OutlineTools` | File symbol tree |
| `search_codebase` | `SearchTools` | Hybrid RRF search |
| `get_chunk` | `ChunkTools` | Fetch by chunk_id |
| `list_collections` | `CollectionsTools` | Collection stats |
| `find_cross_references` | `CrossReferenceTools` | Cross-file / call-site links |
| `map_service_dependencies` | `ServiceMapTools` | Microservice dependency graph |
| `recommend_code` / `find_outlier_chunks` | `RecommendTools` | Vector discovery |
| `expand_search_context` | `ExpandSearchContextTools` | GraphRAG (when `Graph__Enabled`) |
| `get_health` | `HealthTools` | Health |

### Token-efficient workflow

```
1. get_collection_summary
2. search_symbols
3. get_file_outline
4. search_codebase(..., max_content_chars=300)
5. get_chunk(chunk_id)
6. recommend_code / find_outlier_chunks when needed
```

Never call `search_codebase` without `max_content_chars` when you only need locations — use `search_symbols`.

## Conventions

- **Config**: add options in the matching `*Options` class + `appsettings.json` section; override with `Section__Property`. Compose caps stay in `.env` / aspire compose.
- **TEI GPU**: `docker compose $(python scripts/aspire_compose.py)`; `ACCELERATOR=cpu` for CPU-only. Apple Silicon: arm64 TEI image + `--no-gpu-colbert`. External Metal TEI: omit `tei` service, set `Tei__Url=http://host.docker.internal:8080`.
- **Documentation**: when changing MCP tools or deploy env vars, update:
  1. `README.md`
  2. `.github/copilot-instructions.md` (this file)
  3. `skill/codebase-indexer/SKILL.md`
  4. `docs/DEPLOYMENT.md`
  5. Host tool descriptions / MCP instructions in `src/CodebaseIndexer.Host`
- **stdio fallback**: use `CodebaseIndexer.Proxy` — not a Python stdio proxy.

### Key conventions

- **Logging**: use Microsoft.Extensions.Logging / OpenTelemetry (ADR 0018 .NET path). Python `prometheus_client` host metrics were removed with the Python runtime at Phase 7.
- **Config**: `*Options` + validators; env overrides via `Section__Property`.
- **Path normalization**: `index_codebase` accepts folder basename under `WORKSPACE_ROOT` (never `/`).
- **Cross-collection search**: pass multiple collection names in `collections`.
- **TEI GPU / Apple Silicon / Metal**: see Running the server above and `docs/DEPLOYMENT.md`.
- **Documentation sync**: README, this file, SKILL, DEPLOYMENT, Host MCP instructions.

## MCP transport modes

- **HTTP** (default): streamable-HTTP on `127.0.0.1:8000`. Cursor: `"url": "http://localhost:8000/mcp"`. Copilot CLI: `"type": "http"`. Bearer auth when `MCP_AUTH_TOKEN` / Host auth options are set.
- **stdio fallback**: `CodebaseIndexer.Proxy` sidecar — not the MCP container itself.
