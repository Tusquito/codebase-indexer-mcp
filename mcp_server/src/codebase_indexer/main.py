# src/codebase_indexer/main.py
"""FastMCP server entrypoint."""

import logging
import sys
import time

import structlog
from fastmcp import FastMCP
from starlette.requests import Request
from starlette.responses import JSONResponse

from codebase_indexer.config import Settings
from codebase_indexer.index_jobs import IndexJobTracker
from codebase_indexer.indexer.embedder import Embedder
from codebase_indexer.tools.index import register_index_tool
from codebase_indexer.tools.search import register_search_tool
from codebase_indexer.tools.chunk import register_chunk_tool
from codebase_indexer.tools.collections import register_collections_tool
from codebase_indexer.tools.cross_references import register_cross_references_tool
from codebase_indexer.tools.service_map import register_service_map_tool
from codebase_indexer.tools.symbols import register_search_symbols_tool
from codebase_indexer.tools.outline import register_file_outline_tool
from codebase_indexer.tools.summary import register_collection_summary_tool
from codebase_indexer.storage.qdrant import QdrantStorage

settings = Settings()

# Always log to stderr so stdout stays clean for stdio JSON-RPC transport
logging.basicConfig(
    format="%(message)s",
    level=getattr(logging, settings.log_level.upper(), logging.INFO),
    stream=sys.stderr,
)
structlog.configure(
    wrapper_class=structlog.make_filtering_bound_logger(
        getattr(logging, settings.log_level.upper(), logging.INFO)
    ),
    logger_factory=structlog.PrintLoggerFactory(file=sys.stderr),
)
log = structlog.get_logger()

# Pre-load embedding models at startup so indexing/search are instant.
# Models are cached in the fastembed_cache Docker volume.
log.info("preloading_models")
t0 = time.monotonic()
_warmup_embedder = Embedder(
    model=settings.embed_model,
    vector_size=settings.vector_size,
    hybrid=settings.hybrid_search,
)
_warmup_embedder._get_dense_model()
if settings.hybrid_search:
    _warmup_embedder._get_sparse_model()
log.info("models_ready", elapsed=round(time.monotonic() - t0, 2))

mcp = FastMCP(
    name="codebase-indexer",
    instructions="""
    A local codebase semantic search server.
    WORKSPACE_ROOT is mounted as /workspace — each subfolder is a project.

    INDEXING:
    To index a project: index_codebase(path="<project-folder-name>").
    The 'path' MUST be the project folder name (basename of the user's
    working directory). For example, if the user's cwd is
    C:\\Users\\me\\repos\\my-project, pass path="my-project".
    NEVER pass path="/" — that would scan the entire workspace.
    The collection is automatically named after the folder.

    TOKEN-EFFICIENT ORIENTATION (start here for unfamiliar codebases):
    1. get_collection_summary(collection="...") — file counts, language breakdown,
       directory tree, top files. Zero embedding cost. Replaces 3-5 searches.
    2. search_symbols(query="...") — find WHERE symbols exist without code content.
       Use before search_codebase when you only need locations, not implementations.
    3. get_file_outline(rel_path="...", collection="...") — symbol tree for a file.
       Know what's in a file before fetching any chunk content.
    4. search_codebase(..., max_content_chars=300) — truncate chunk content.
       Call get_chunk for the 1-2 results you actually need in full.

    SEARCHING:
    Set 'collection' to the project folder name (basename of the
    user's working directory). Pass additional project names in
    'collections' to search across multiple indexed projects.
    Use list_collections() to see all indexed projects.

    CROSS-REFERENCES:
    Use find_cross_references(query="...", collections=[...]) to
    discover links between projects (imports, HTTP calls, shared types).

    SERVICE MAPPING:
    Use map_service_dependencies(collections=[...]) to automatically
    build a full dependency graph across microservices. Discovers
    endpoint definitions, HTTP clients, and config-based URLs, then
    matches them to produce a call chain map.

    Search uses nomic-embed-text (dense ONNX) + BM25 (sparse) fused via RRF.
    """,
)


@mcp.custom_route("/health", methods=["GET"])
async def health(request: Request) -> JSONResponse:
    return JSONResponse({
        "status": "ok",
        "model": settings.embed_model,
        "hybrid": settings.hybrid_search,
        "vector_size": settings.vector_size,
    })


storage = QdrantStorage(settings)
job_tracker = IndexJobTracker()
register_index_tool(mcp, settings, storage, job_tracker)
register_search_tool(mcp, settings, storage)
register_chunk_tool(mcp, settings, storage)
register_collections_tool(mcp, settings, storage)
register_cross_references_tool(mcp, settings, storage)
register_service_map_tool(mcp, settings, storage)
register_search_symbols_tool(mcp, settings, storage)
register_file_outline_tool(mcp, settings, storage)
register_collection_summary_tool(mcp, settings, storage)

if __name__ == "__main__":
    log.info("starting_mcp_server", transport=settings.mcp_transport, port=settings.mcp_port)
    if settings.mcp_transport == "stdio":
        mcp.run(transport="stdio")
    else:
        mcp.run(
            transport="streamable-http",
            host=settings.mcp_host,
            port=settings.mcp_port,
        )
