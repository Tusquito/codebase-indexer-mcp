# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Call-site cross-references** — chunks now store a `callees` payload (bare method names and `receiver.method` tokens). `find_cross_references` accepts optional `member` and `receiver` params and returns `call_site` matches via exact callee filter (not semantic search), including same-collection consumer links for inherited-field call sites (e.g. Spring `@Autowired` fields used in subclasses).

### Changed

- **Forced re-index required for `callees`** — existing collections need `index_codebase(..., force=True)` or `index_all(force=True)` to backfill `callees` and build the new keyword index; incremental re-index alone skips unchanged files and payloads are schemaless with no collection schema-version metadata.

### Fixed

- **Member-only queries** — `find_cross_references` accepts `member` alone; no `query` or `symbol_name` required for exact call-site lookup.
- **Call-site match promotion** — when a chunk is already in results (e.g. from import search), the callees path promotes it to `call_site`; `top_k` no longer hides call sites behind import rows.
- **Code dependency links** — passing `symbol_name` with `member` populates `links[]` with `code_dependency` edges from call sites to the matching definition.
- **Cursor MCP connection** — document native HTTP transport (`"url": "http://localhost:8000/mcp"`) as the recommended client config; reconnects automatically after `mcp_server` restarts without a manual MCP reload. Deprecated `docker exec` into `codeindexer_mcp` (stdio pipe broke on every container restart).
- **`uv run` stdio startup** — removed `readme = "../README.md"` from `mcp_server/pyproject.toml` so `uv run` no longer fails with `OSError: Readme file does not exist` when re-syncing the editable package inside the container. Stdio fallback now uses the sidecar proxy (`codeindexer_proxy`) instead of exec into the main container.

## [0.1.0] - 2026-06-05

### Added

- **Hybrid semantic search** — dense ONNX embeddings (`DENSE_EMBED_MODEL`) fused with sparse keyword matching (`SPARSE_EMBED_MODEL`, default `Qdrant/bm25`) via reciprocal rank fusion (RRF) when `HYBRID_SEARCH=true`
- **AST-based chunking** — Tree-sitter parsing for Python, JavaScript, TypeScript, Go, Rust, Java, C, C++, and C#; sliding-window fallback for markup and unsupported languages
- **Incremental indexing** — SHA-256 and mtime pre-filters skip unchanged files; stale chunks purged after scan
- **12 MCP tools** — `index_codebase`, `index_status`, `index_all`, `stop_indexing`, `get_collection_summary`, `search_symbols`, `get_file_outline`, `search_codebase`, `get_chunk`, `list_collections`, `find_cross_references`, `map_service_dependencies`
- **Multi-arch Docker** — CPU (default), NVIDIA CUDA (`docker-compose.gpu.yml`), AMD ROCm native (`docker-compose.amd.yml`), and AMD ROCm WSL2 (`docker-compose.amd.wsl2.yml`) compose overrides
- **Scheduled reindex** — `codeindexer_cron` service runs `cron/reindex.py` to git-fetch default branches and trigger incremental re-index when repos change
- **Token-efficient orientation tools** — `get_collection_summary`, `search_symbols`, and `get_file_outline` use Qdrant payload scroll only (zero embedding cost)
- **Cross-collection analysis** — `find_cross_references` and `map_service_dependencies` discover HTTP call chains and build-level dependencies (Maven, NuGet, npm, Gradle, Go, Cargo, Python)
- **stdio proxy** — thin JSON-RPC shim (`codebase_indexer.stdio_proxy`) forwards stdio clients to the in-container HTTP server without reloading models

[0.1.0]: https://github.com/your-org/codebase-indexer-mcp/releases/tag/v0.1.0
