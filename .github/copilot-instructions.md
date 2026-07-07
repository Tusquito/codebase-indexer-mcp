# Codebase Indexer MCP — Copilot Instructions

## What this project is

A fully self-hosted MCP server that indexes codebases into a local Qdrant vector database using **TEI for dense embeddings** and in-process BM25 sparse search, then exposes semantic search tools to AI agents. TEI must be reachable for indexing and search.

## Running the server

```bash
# Start services (GPU default — bundled TEI profile)
docker compose $(python scripts/compose_files.py) --profile bundled-tei up -d --build

# Explicit CPU-only (CI / no NVIDIA)
docker compose $(ACCELERATOR=cpu python scripts/compose_files.py) --profile bundled-tei up -d --build

# Check health
curl http://localhost:8000/health

# View MCP server logs
docker logs -f codeindexer_mcp
```

All config is env-var driven via `.env` (copy from `.env.example`). **Required in `.env` (no Python defaults):** `WORKSPACE_ROOT`, `MCP_MEM_LIMIT`, `QDRANT_MEM_LIMIT`, `MCP_CPUS`, `QDRANT_CPUS`, `OMP_NUM_THREADS`, `ACCELERATOR`, `DENSE_EMBED_MODEL`, `SPARSE_EMBED_MODEL`, `DENSE_EMBED_VECTOR_SIZE`, `SPARSE_THREADS`. **TEI (dense):** `TEI_URL`, `DENSE_EMBED_MODEL`, `COMPOSE_PROFILES=bundled-tei`, `TEI_GPU=1` with GPU compose merged by `scripts/compose_files.py` when `ACCELERATOR=gpu` (default). Set `ACCELERATOR=cpu` for CPU-only hosts. Optional: `HYBRID_SEARCH`, `MCP_TRANSPORT`, pipeline/storage knobs in `config.py`. Default sparse is `Qdrant/bm25` with `SPARSE_THREADS=2` (always CPU in MCP — see [ADR 0022](docs/adr/0022-gpu-default-cpu-fallback.md)). Performance/RAM knobs: batching `BATCH_SIZE`/`FLUSH_EVERY`/`UPSERT_BATCH`/`READAHEAD_BUFFER`/`MAX_DENSE_EMBED_TOKENS`/`MAX_SPARSE_EMBED_TOKENS`, `SEQUENTIAL_EMBED`, glibc `MALLOC_ARENA_MAX`/`MALLOC_TRIM_THRESHOLD_`, Qdrant storage `VECTORS_ON_DISK`/`SPARSE_ON_DISK`/`QUANTIZATION`/`MEMMAP_THRESHOLD_KB`, memory pressure `MEMORY_PRESSURE_WARN_PCT`/`MEMORY_PRESSURE_HALT_PCT`, and model lifecycle `RELEASE_MODELS_AFTER_INDEX`/`MODEL_IDLE_TIMEOUT`. See [ADR 0025](docs/adr/0025-huggingface-tei-dense-embedding.md).

**Important:** `MCP_MEM_LIMIT + QDRANT_MEM_LIMIT` must leave at least 2–3 GiB for the Linux kernel, Docker daemon, and WSL2 overhead. Over-allocating causes silent OOM kills.

## Development

All Python code lives in `mcp_server/`. The package manager is `uv`.

```bash
cd mcp_server

# Install deps (including dev)
uv sync

# Lint
uv run ruff check .

# Type check
uv run mypy src/

# Run single test file
uv run pytest tests/test_foo.py -v

# Run server locally (against a running Qdrant)
uv run python -m codebase_indexer.main
```

## Architecture

Three Docker services:
- **Qdrant** (`codeindexer_qdrant`, port 6333/6334) — vector database, persistent via `qdrant_data` volume
- **MCP server** (`codeindexer_mcp`, port 8000) — FastMCP server, the only service with business logic
- **Cron** (`codeindexer_cron`) — scheduled git pull + incremental re-index via `cron/reindex.py`; no host ports

`WORKSPACE_ROOT` on the host is mounted read-only into the container at `/workspace`. Each direct subdirectory of `/workspace` is one **collection** (indexed project). The collection name is always the folder basename.

### Indexing pipeline (`indexer/`)

`scan_files` → `chunk_file` → `Embedder.embed_chunks` → `QdrantStorage.upsert_chunks`

1. **Scanner** (`scanner.py`) — walks the filesystem, skips dirs in `EXCLUDED_DIRS`, respects `.gitignore` and `.codeindexignore`, detects language by extension.
2. **Chunker** (`chunker.py`) — uses Tree-sitter AST for supported languages (Python, JS/TS, Go, Rust, Java, C/C++, C#); falls back to sliding window for unsupported languages and markup files. Chunk IDs are deterministic: `sha256("{rel_path}:{start_line}")`.
3. **Embedder** (`embedder.py`) — **TEI HTTP dense** (`DENSE_EMBED_MODEL`, `TEI_URL`) + sparse lexical BM25 (`SPARSE_EMBED_MODEL`, default `Qdrant/bm25`) in-process. Sparse encoding runs on CPU via fastembed ONNX. `MAX_DENSE_EMBED_TOKENS` caps text sent to TEI (word-split approximation; `0` auto-detects from `DENSE_EMBED_MODEL` registry). `MAX_SPARSE_EMBED_TOKENS` (default `0` = no sparse truncation for BM25). `SEQUENTIAL_EMBED` forces sparse-then-dense during indexing. Sparse BM25 is a **class-level singleton** released via `Embedder.release_models()`; TEI has no in-process dense model. **Memory guard**: cgroup pressure can force sequential sparse+dense embedding. **Idle-timeout**: releases sparse BM25 after `MODEL_IDLE_TIMEOUT` seconds of no embed activity.
4. **Pipeline** (`pipeline.py`) — double-buffered flushing: while Qdrant ingests batch N (I/O-bound), the CPU embeds batch N+1. Flushes every `FLUSH_EVERY` chunks (default 1500) to keep memory bounded, calls `trim_memory()` after each upsert completes, and logs `rss_mb`. HNSW indexing is deferred during bulk upload (`QdrantStorage.set_indexing`) and restored in a `finally`. **Memory guard**: checks cgroup memory pressure before each flush and aborts with a clear error instead of being OOM-killed silently. **Post-pipeline cleanup**: after the pipeline finishes, runs `gc.collect()` + `trim_memory()` unconditionally and logs RSS before/after — ensures transient allocations (chunk lists, numpy arrays) are returned to the OS regardless of whether models are released.
5. **Memory** (`memory.py`) — cgroup-aware memory utilities. Reads cgroup v2/v1 memory limits and current usage. Provides `check_memory_pressure()` with configurable warn/halt thresholds for adaptive OOM prevention.

Incremental indexing: existing SHA-256 hashes are fetched from Qdrant before the scan; unchanged files are skipped; stale files (deleted from disk) are purged after the scan.

### MCP tools (`tools/`)

Each tool is a `register_*_tool(mcp, ctx)` function — register all of them in `main.py`. Do not add tool logic directly to `main.py`.

| Tool | File | Description |
|------|------|-------------|
| `index_codebase` / `index_status` | `index.py` | Indexes a project; blocks until done by default (`wait=True`). Pass `wait=False` for fire-and-forget; then use `index_status` to poll. Jobs tracked in `IndexJobTracker` |
| `stop_indexing` | `index.py` | Gracefully cancel a running indexing job |
| `index_all` | `index.py` | Re-indexes all existing collections sequentially. Discovers collections via `storage.list_collection_stats()`, skips already-running jobs, continues on failure. Same params as `index_codebase` minus `path`/`collection`. |
| `get_collection_summary` | `summary.py` | **Token-efficient orientation**: language breakdown, dir tree, top files, and `build_dependencies` (which other indexed collections this project depends on via Maven/NuGet/npm/Gradle/Go/Cargo/Python). Zero embedding cost. Call first on an unfamiliar codebase. |
| `search_symbols` | `symbols.py` | **Token-efficient symbol lookup**: same hybrid search as `search_codebase` but returns only metadata (no code content). `top_k` capped at 30. When `HYBRID_SEARCH` is on (default), RRF ranking applies and `min_score` is ignored; see `docs/SEARCH_BEHAVIOR.md`. Saves ~90% tokens vs `search_codebase`. |
| `get_file_outline` | `outline.py` | **Token-efficient file structure**: symbol tree for a specific file via Qdrant scroll (zero embedding cost). Know what's in a file before fetching any chunk. |
| `search_codebase` | `search.py` | Hybrid RRF search (dense + sparse) across one or more collections. `top_k` capped at 20. When `HYBRID_SEARCH` is on (default), RRF ranking applies and `min_score` is ignored; see `docs/SEARCH_BEHAVIOR.md`. Use `max_content_chars` to truncate results; then call `get_chunk` for the 1–2 results you actually need in full. |
| `get_chunk` | `chunk.py` | Retrieve a specific chunk by ID — use after `search_symbols` or truncated `search_codebase` |
| `list_collections` | `collections.py` | List all indexed collections with stats |
| `find_cross_references` | `cross_references.py` | Discover symbol/endpoint links across collections. Reference types: `definition`, `import`, `usage`, `endpoint_definition`, `http_call`, `service_config`, `build_dependency`, `call_site`. Optional `member`/`receiver` for exact call-site lookup. When `RERANK_ENABLED=true`, semantic paths use ColBERT rerank |
| `map_service_dependencies` | `service_map.py` | Build a full microservice dependency graph. Detects HTTP call chains **and** build-level dependencies (Maven, NuGet, npm, Gradle, Go, Cargo, Python). When `RERANK_ENABLED=true`, batched discovery uses ColBERT rerank |
| `recommend_code` | `recommend.py` | **Vector discovery**: chunks similar to positive examples and dissimilar from negatives (Qdrant Recommendation API, dense-only). Gated by `RECOMMEND_ENABLED` (default on); capped by `RECOMMEND_MAX_EXAMPLES`. Single collection; see `docs/SEARCH_BEHAVIOR.md` |
| `find_outlier_chunks` | `recommend.py` | **Vector discovery**: chunks semantically distant from a module context (`BEST_SCORE`, negative-only) + cosine-to-centroid filter. Gated by `RECOMMEND_ENABLED` (default on); see `docs/SEARCH_BEHAVIOR.md` |

### Token-efficient workflow

Always follow this order to minimize tokens consumed per task:

```
1. get_collection_summary   → orient (language, dirs, top files)   [zero embed cost]
2. search_symbols           → locate symbols (no code content)      [~90% token savings]
3. get_file_outline         → inspect file structure                [zero embed cost]
4. search_codebase(..., max_content_chars=300) → narrow candidates  [truncated content]
5. get_chunk(chunk_id)      → read only the 1-2 chunks you need     [full content]
6. recommend_code(...)      → "like this, not that" discovery       [embed per text example]
7. find_outlier_chunks(...) → semantically distant code in a module  [embed per text example]
```

For **similar-to-X but not-in-tests** discovery, prefer `recommend_code` over a negated `search_codebase` query.

For **dead code / refactor triage** (chunks unlike the rest of a module), prefer `find_outlier_chunks` with `path_glob` or `context_chunk_ids`.

Never call `search_codebase` without `max_content_chars` when you only need symbol locations — use `search_symbols` instead.

### Key conventions

- **Logging**: always use `structlog` and always log to **`stderr`**. Stdout is reserved for the stdio JSON-RPC transport. Never use `print()`.
- **Async CPU work**: wrap all CPU-bound calls (embedding, Tree-sitter parsing) in `loop.run_in_executor(None, sync_fn, ...)` — they must not block the event loop.
- **Settings**: add new config in `config.py` as a `pydantic-settings` field. It will automatically read from the matching env var (case-insensitive) or `.env`. Also add `${VAR:-default}` to the `mcp_server.environment` block in `docker-compose.yml` so Docker deployments pick it up.
- **Path normalization**: `index_codebase` accepts full host paths (e.g. `C:\Users\me\repos\my-project`) and normalizes them to the last path component. Never pass `/` as the indexing path.
- **Chunk sizes**: verbose/markup languages (`xml`, `yaml`, `json`, `markdown`, etc.) are capped at 60 lines per chunk; all others use `MAX_CHUNK_LINES` (default 150).
- **Cross-collection search**: pass multiple collection names in the `collections` parameter of `search_codebase` / `find_cross_references`. Single-collection search goes through a faster code path.
- **Build dependency detection**: `tools/build_deps.py` provides `extract_build_deps(content, rel_path)`, `is_build_manifest(rel_path)`, and `match_deps_to_collections(deps, collection_names)`. These parse Maven/NuGet/npm/Gradle/Go/Cargo/Python manifests and fuzzy-match artifact names against indexed collection names (e.g. artifact `my-core-definitions` matches collection `my-core`). Reference type `build_dependency` is returned by `find_cross_references` for manifest files. `map_service_dependencies` adds a Phase 2b that emits `build_dependency` edges. `get_collection_summary` auto-detects and reports `build_dependencies` when other collections are indexed.
- **TEI GPU**: default when `ACCELERATOR=gpu` — use `docker compose $(python scripts/compose_files.py)`; set `ACCELERATOR=cpu` for CPU-only. See [ADR 0022](docs/adr/0022-gpu-default-cpu-fallback.md), [ADR 0025](docs/adr/0025-huggingface-tei-dense-embedding.md).
- **Documentation**: whenever you add, remove, or change an MCP tool (signature, behaviour, description), you **must** also update:
  1. `README.md` — the tool table and any relevant sections (Quick Start, Configuration, Architecture)
  2. `.github/copilot-instructions.md` — the tool table and Key conventions
  3. `skill/codebase-indexer/SKILL.md` — tool ladder / quick reference when user-facing workflow changes
  4. `mcp_server/src/codebase_indexer/main.py` — `_INSTRUCTIONS` for MCP clients

## MCP transport modes

- **HTTP** (default, recommended): `streamable-http` on `127.0.0.1:8000`. Cursor connects via `"url": "http://localhost:8000/mcp"` in `mcp.json` (auto-reconnects after container restarts). Claude Desktop may also need `"transport": "streamable-http"`. **Copilot CLI** uses `~/.copilot/mcp-config.json` with `"type": "http"` (not `"type": "sse"` — SSE causes HTTP 406 because it sends an incompatible `Accept` header). When `MCP_AUTH_TOKEN` is set, clients pass `headers.Authorization: Bearer <token>`.
- **stdio fallback**: uncomment the `codeindexer_proxy` sidecar in `docker-compose.yml` and `docker exec` into it — not `codeindexer_mcp`. Use when localhost HTTP is blocked or a client requires stdio. Do **not** use `docker exec … uv run … stdio_proxy` on the main container (deprecated: `uv run` startup failure + stdio pipe breaks on restart).
