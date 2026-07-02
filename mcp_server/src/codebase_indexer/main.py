# src/codebase_indexer/main.py
"""FastMCP server entrypoint.

Importing this module has no side effects: all wiring happens in create_app(),
so the module is safe to import in tests without loading models or settings.
"""

import hmac
import logging
import os
import sys
import time

import structlog
from fastmcp import FastMCP
from starlette.middleware import Middleware
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.requests import Request
from starlette.responses import JSONResponse

from codebase_indexer.config import Settings
from codebase_indexer.context import AppContext
from codebase_indexer.memory import (
    get_cgroup_memory_limit,
    log_memory_diagnostics,
)
from codebase_indexer.tools.index import register_index_tool
from codebase_indexer.tools.search import register_search_tool
from codebase_indexer.tools.chunk import register_chunk_tool
from codebase_indexer.tools.collections import register_collections_tool
from codebase_indexer.tools.cross_references import register_cross_references_tool
from codebase_indexer.tools.service_map import register_service_map_tool
from codebase_indexer.tools.symbols import register_search_symbols_tool
from codebase_indexer.tools.outline import register_file_outline_tool
from codebase_indexer.tools.summary import register_collection_summary_tool

_INSTRUCTIONS = """
    A local codebase semantic search server.
    WORKSPACE_ROOT is mounted as /workspace — each subfolder is a project.

    INDEXING:
    To index a project: index_codebase(path="<project-folder-name>").
    The 'path' MUST be the project folder name (basename of the user's
    working directory). For example, if the user's cwd is
    C:\\Users\\me\\repos\\my-project, pass path="my-project".
    NEVER pass path="/" — that would scan the entire workspace.
    The collection is automatically named after the folder.

    To re-index all existing collections: index_all().
    Runs sequentially (one at a time) for memory safety.
    Use force=True for a full re-index instead of incremental.

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

    Search uses OLLAMA_EMBED_MODEL (dense via Ollama HTTP) + SPARSE_EMBED_MODEL (sparse BM25)
    fused via RRF when HYBRID_SEARCH is enabled.
    """


def configure_logging(settings: Settings) -> None:
    """Route all logs to stderr so stdout stays clean for stdio JSON-RPC."""
    level = getattr(logging, settings.log_level.upper(), logging.INFO)
    logging.basicConfig(format="%(message)s", level=level, stream=sys.stderr)
    structlog.configure(
        wrapper_class=structlog.make_filtering_bound_logger(level),
        logger_factory=structlog.PrintLoggerFactory(file=sys.stderr),
    )


class BearerAuthMiddleware(BaseHTTPMiddleware):
    """Require `Authorization: Bearer <token>` on every route except /health."""

    def __init__(self, app, token: str) -> None:
        super().__init__(app)
        self._expected = f"Bearer {token}"

    async def dispatch(self, request: Request, call_next):
        if request.url.path == "/health":
            return await call_next(request)
        provided = request.headers.get("Authorization", "")
        # Constant-time compare to avoid leaking the token via timing.
        if not hmac.compare_digest(provided, self._expected):
            return JSONResponse({"error": "unauthorized"}, status_code=401)
        return await call_next(request)


def create_app(settings: Settings | None = None, preload_models: bool | None = None) -> FastMCP:
    """Build and wire the FastMCP server.

    All side effects (logging setup, model preload, tool registration) live here
    rather than at import time, so the module stays import-safe for tests.
    """
    settings = settings or Settings()
    configure_logging(settings)
    log = structlog.get_logger()

    # --- Startup memory diagnostics & OOM-restart detection ---
    _CLEAN_SHUTDOWN_MARKER = "/tmp/.mcp_clean_shutdown"
    if os.path.exists(_CLEAN_SHUTDOWN_MARKER):
        os.remove(_CLEAN_SHUTDOWN_MARKER)
    else:
        # Marker absent → previous instance didn't shut down cleanly.
        # First boot has no marker either, so only warn when the container
        # has been running before (restart count > 0 implies prior crash).
        log.warning(
            "possible_oom_restart",
            msg="Previous instance may have been OOM-killed (no clean shutdown marker found).",
            hint="Check 'docker inspect' for RestartCount and OOMKilled.",
        )

    log_memory_diagnostics("startup")
    limit = get_cgroup_memory_limit()
    if limit:
        limit_gb = round(limit / (1024**3), 1)
        log.info(
            "memory_config",
            cgroup_limit_gb=limit_gb,
            batch_size=settings.batch_size,
            flush_every=settings.flush_every,
            max_dense_embed_tokens=settings.max_dense_embed_tokens,
            max_sparse_embed_tokens=settings.max_sparse_embed_tokens,
            warn_pct=settings.memory_pressure_warn_pct,
            halt_pct=settings.memory_pressure_halt_pct,
        )
        # Dense runs in Ollama; MCP peak RSS is typically a few hundred MB (see pipeline logs).
        if limit_gb < 1.5:
            log.warning(
                "low_memory_limit",
                cgroup_limit_gb=limit_gb,
                hint="Consider reducing BATCH_SIZE, FLUSH_EVERY, MAX_DENSE_EMBED_TOKENS, or MAX_SPARSE_EMBED_TOKENS for stability.",
            )

    # Write clean shutdown marker on exit (best effort)
    import atexit
    atexit.register(lambda: open(_CLEAN_SHUTDOWN_MARKER, "w").write("ok"))

    ctx = AppContext.create(settings)

    # Configure idle-timeout so _ensure_idle_timer() picks it up lazily
    # on the first embed call (avoids needing an ASGI lifecycle hook).
    from codebase_indexer.indexer.embedder import Embedder as _Embedder
    _Embedder._idle_timeout_s = settings.model_idle_timeout
    if settings.model_idle_timeout > 0:
        log.info("idle_timer_configured", timeout_s=settings.model_idle_timeout)

    if preload_models is None:
        preload_models = settings.preload_models

    if preload_models:
        log.info(
            "preloading_models",
            dense_embed_backend="ollama",
        )
        t0 = time.monotonic()
        try:
            ctx.embedder.preload()
            log.info("models_ready", elapsed=round(time.monotonic() - t0, 2))
        except Exception as exc:
            log.warning(
                "model_preload_failed_continuing",
                error=str(exc),
                error_type=type(exc).__name__,
            )

    mcp = FastMCP(name="codebase-indexer", instructions=_INSTRUCTIONS)

    @mcp.custom_route("/health", methods=["GET"])
    async def health(request: Request) -> JSONResponse:
        # Minimal, unauthenticated liveness probe. Deliberately does NOT echo the
        # model/config so the endpoint cannot be used to fingerprint the server.
        return JSONResponse({"status": "ok"})

    register_index_tool(mcp, ctx)
    register_search_tool(mcp, ctx)
    register_chunk_tool(mcp, ctx)
    register_collections_tool(mcp, ctx)
    register_cross_references_tool(mcp, ctx)
    register_service_map_tool(mcp, ctx)
    register_search_symbols_tool(mcp, ctx)
    register_file_outline_tool(mcp, ctx)
    register_collection_summary_tool(mcp, ctx)

    return mcp


def main() -> None:
    import os

    from codebase_indexer.indexer.backends.onnx_common import resolve_threads

    settings = Settings()
    mcp = create_app(settings)
    log = structlog.get_logger()
    if not os.environ.get("OMP_NUM_THREADS"):
        resolved = resolve_threads(0)
        log.info(
            "onnx_threads_auto",
            omp_num_threads=resolved,
            sparse_threads=settings.sparse_threads,
            hint="Set OMP_NUM_THREADS to tune sparse BM25 threads",
        )
    log.info("starting_mcp_server", transport=settings.mcp_transport, port=settings.mcp_port)
    if settings.mcp_transport == "stdio":
        mcp.run(transport="stdio")
    else:
        # Only attach auth middleware when a token is configured, so default
        # local deployments (protected by the 127.0.0.1 port binding) are
        # unaffected.
        http_kwargs: dict = {}
        if settings.mcp_auth_token:
            log.info("bearer_auth_enabled")
            http_kwargs["middleware"] = [
                Middleware(BearerAuthMiddleware, token=settings.mcp_auth_token)
            ]
        mcp.run(
            transport="streamable-http",
            host=settings.mcp_host,
            port=settings.mcp_port,
            **http_kwargs,
        )


if __name__ == "__main__":
    main()
