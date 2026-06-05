# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
