# Codebase Indexer MCP — Copilot Instructions

## What this project is

A fully self-hosted MCP server that indexes codebases into a local Qdrant vector database using fastembed ONNX embeddings, then exposes semantic search tools to AI agents. No external API calls — everything runs in Docker.

## Running the server

```bash
# Start all services (Qdrant + MCP server)
docker compose up -d --build

# Check health
curl http://localhost:8000/health

# View MCP server logs
docker logs -f codeindexer_mcp
```

All config is env-var driven via `.env` (copy from `.env.example`). Key vars: `WORKSPACE_ROOT`, `EMBED_MODEL`, `HYBRID_SEARCH`, `MCP_TRANSPORT`.

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

`WORKSPACE_ROOT` on the host is mounted read-only into the container at `/workspace`. Each direct subdirectory of `/workspace` is one **collection** (indexed project). The collection name is always the folder basename.

### Indexing pipeline (`indexer/`)

`scan_files` → `chunk_file` → `Embedder.embed_chunks` → `QdrantStorage.upsert_chunks`

1. **Scanner** (`scanner.py`) — walks the filesystem, skips dirs in `EXCLUDED_DIRS`, respects `.gitignore` and `.codeindexignore`, detects language by extension.
2. **Chunker** (`chunker.py`) — uses Tree-sitter AST for supported languages (Python, JS/TS, Go, Rust, Java, C/C++, C#); falls back to sliding window for unsupported languages and markup files. Chunk IDs are deterministic: `sha256("{rel_path}:{start_line}")`.
3. **Embedder** (`embedder.py`) — fastembed ONNX for dense vectors (nomic-embed-text-v1.5, 768-dim) + BM25 sparse vectors. Models are **class-level singletons** loaded once and released after each indexing job via `Embedder.release_models()` to reclaim native memory.
4. **Pipeline** (`pipeline.py`) — double-buffered flushing: while Qdrant ingests batch N (I/O-bound), the CPU embeds batch N+1. Flushes every 500 chunks to keep memory bounded.

Incremental indexing: existing SHA-256 hashes are fetched from Qdrant before the scan; unchanged files are skipped; stale files (deleted from disk) are purged after the scan.

### MCP tools (`tools/`)

Each tool is a `register_*_tool(mcp, settings, storage)` function — register all of them in `main.py`. Do not add tool logic directly to `main.py`.

| Tool | File | Description |
|------|------|-------------|
| `index_codebase` / `index_status` | `index.py` | Async background indexing; jobs tracked in `IndexJobTracker` |
| `get_collection_summary` | `summary.py` | **Token-efficient orientation**: language breakdown, dir tree, top files. Zero embedding cost. Call first on an unfamiliar codebase. |
| `search_symbols` | `symbols.py` | **Token-efficient symbol lookup**: same hybrid search as `search_codebase` but returns only metadata (no code content). Use to locate symbols before fetching content. Saves ~90% tokens vs `search_codebase`. |
| `get_file_outline` | `outline.py` | **Token-efficient file structure**: symbol tree for a specific file via Qdrant scroll (zero embedding cost). Know what's in a file before fetching any chunk. |
| `search_codebase` | `search.py` | Hybrid RRF search (dense + sparse) across one or more collections. Use `max_content_chars` to truncate results; then call `get_chunk` for the 1–2 results you actually need in full. |
| `get_chunk` | `chunk.py` | Retrieve a specific chunk by ID — use after `search_symbols` or truncated `search_codebase` |
| `list_collections` | `collections.py` | List all indexed collections with stats |
| `find_cross_references` | `cross_references.py` | Discover symbol/endpoint links across collections |
| `map_service_dependencies` | `service_map.py` | Build a full microservice dependency graph |

### Token-efficient workflow

Always follow this order to minimize tokens consumed per task:

```
1. get_collection_summary   → orient (language, dirs, top files)   [zero embed cost]
2. search_symbols           → locate symbols (no code content)      [~90% token savings]
3. get_file_outline         → inspect file structure                [zero embed cost]
4. search_codebase(..., max_content_chars=300) → narrow candidates  [truncated content]
5. get_chunk(chunk_id)      → read only the 1-2 chunks you need     [full content]
```

Never call `search_codebase` without `max_content_chars` when you only need symbol locations — use `search_symbols` instead.

### Key conventions

- **Logging**: always use `structlog` and always log to **`stderr`**. Stdout is reserved for the stdio JSON-RPC transport. Never use `print()`.
- **Async CPU work**: wrap all CPU-bound calls (embedding, Tree-sitter parsing) in `loop.run_in_executor(None, sync_fn, ...)` — they must not block the event loop.
- **Settings**: add new config in `config.py` as a `pydantic-settings` field. It will automatically read from the matching env var (case-insensitive) or `.env`.
- **Path normalization**: `index_codebase` accepts full host paths (e.g. `C:\Users\me\repos\my-project`) and normalizes them to the last path component. Never pass `/` as the indexing path.
- **Chunk sizes**: verbose/markup languages (`xml`, `yaml`, `json`, `markdown`, etc.) are capped at 60 lines per chunk; all others use `MAX_CHUNK_LINES` (default 150).
- **Cross-collection search**: pass multiple collection names in the `collections` parameter of `search_codebase` / `find_cross_references`. Single-collection search goes through a faster code path.

## MCP transport modes

- **HTTP** (default): `streamable-http` on port 8000. Used by Claude Desktop and Copilot CLI.
- **stdio**: set `MCP_TRANSPORT=stdio` and use the Docker exec command. Used by Cursor/Windsurf.
