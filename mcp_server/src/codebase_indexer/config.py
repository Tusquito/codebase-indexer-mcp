# src/codebase_indexer/config.py
from typing import Self

from pydantic import Field, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

# Single source of truth for the default URL-path keywords used by the
# service-mapping / cross-reference URL extractors. Both Settings (below) and
# the extractor defaults derive from this, so there is only one place to edit.
DEFAULT_SERVICE_URL_KEYWORDS = (
    "rest,api,profile,service,internal,public,gateway,graphql,webhook,auth,users,accounts"
)

# BAAI BGE English v1.5 — official (dimension, max sequence length in tokens).
# https://huggingface.co/BAAI/bge-base-en-v1.5
BGE_EN_V1_5_SPECS: dict[str, tuple[int, int]] = {
    "BAAI/bge-base-en-v1.5": (768, 512),
    "BAAI/bge-small-en-v1.5": (384, 512),
}

# Known dense embedding models and their output dimensions. When listed,
# DENSE_EMBED_VECTOR_SIZE must match exactly (set both in .env — see .env.example).
KNOWN_EMBED_MODEL_DIMENSIONS: dict[str, int] = {
    "nomic-ai/nomic-embed-text-v1.5": 768,
    **{model: dims for model, (dims, _) in BGE_EN_V1_5_SPECS.items()},
}

# Known dense transformer models and max input tokens (embedding truncation).
KNOWN_EMBED_MODEL_MAX_TOKENS: dict[str, int] = {
    "nomic-ai/nomic-embed-text-v1.5": 8192,
    **{model: max_tokens for model, (_, max_tokens) in BGE_EN_V1_5_SPECS.items()},
}


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=".env",
        case_sensitive=False,
        extra="ignore",
    )

    qdrant_url: str = Field(default="http://localhost:6333")
    # Timeout (seconds) for Qdrant client calls. A hung Qdrant then surfaces as
    # a clean timeout error instead of stalling the asyncio event loop.
    qdrant_timeout: float = Field(default=30.0)
    qdrant_collection: str = Field(default="codebase")
    # No Python defaults — set DENSE_EMBED_MODEL, SPARSE_EMBED_MODEL,
    # DENSE_EMBED_VECTOR_SIZE, SPARSE_THREADS in .env (sparse threads depend on
    # SPARSE_EMBED_MODEL — default Qdrant/bm25: SPARSE_THREADS=2).
    dense_embed_model: str
    sparse_embed_model: str
    dense_embed_vector_size: int
    sparse_threads: int
    hybrid_search: bool = Field(default=True)
    max_chunk_lines: int = Field(default=150)
    chunk_overlap_lines: int = Field(default=20)
    batch_size: int = Field(default=32)
    mcp_transport: str = Field(default="streamable-http")
    mcp_host: str = Field(default="0.0.0.0")
    mcp_port: int = Field(default=8000)
    # Optional bearer token for the HTTP transport. When set, every request
    # except /health must send `Authorization: Bearer <token>`. Empty disables
    # auth (rely on the 127.0.0.1 port binding for local-only deployments).
    mcp_auth_token: str = Field(default="")
    workspace_path: str = Field(default="/workspace")
    log_level: str = Field(default="INFO")

    # Directory names pruned during the scan walk (build artifacts, VCS, caches,
    # editor metadata). Comma-separated and env-overridable. Project-specific or
    # content-bearing folders (e.g. docs) belong in a per-project
    # .codeindexignore rather than this global default.
    excluded_dirs: str = Field(
        default=(
            "node_modules,.git,__pycache__,.venv,venv,dist,build,target,bin,obj,"
            ".gradle,.mypy_cache,.pytest_cache,.ruff_cache,.github,.idea,.vscode"
        )
    )

    # Release the shared ONNX models after an indexing job completes. Reclaims
    # ~300-500 MB of native ONNX/glibc memory immediately. Models reload in
    # ~1.5s from the fastembed_cache volume on the next search query.
    # Default on: indexing is infrequent, idle RAM costs more than reload latency.
    # Set to false only if you need sub-second first-search latency after indexing.
    release_models_after_index: bool = Field(default=True)

    # Seconds of embed inactivity before ONNX models are automatically released.
    # Covers cases where models were loaded for search but the server goes idle.
    # 0 disables the idle timer (models stay until process restart or explicit release).
    model_idle_timeout: int = Field(default=300)

    # --- Pipeline tuning knobs (hardware-portable; all env-overridable) ---
    # Number of chunks accumulated before an embed+upsert flush. The double
    # buffer keeps at most 2 of these in memory, so peak RAM scales with this.
    flush_every: int = Field(default=1500)
    # Points per Qdrant upsert sub-batch (stays within gRPC/HTTP size limits).
    upsert_batch: int = Field(default=500)
    # How many scanned files may be queued ahead of the consumer.
    readahead_buffer: int = Field(default=100)
    # Max tokens fed to the dense encoder. 0 = auto-detect from model (recommended).
    # BGE base/small v1.5 auto-detect to 512; nomic v1.5 to 8192. Set lower to
    # reduce ONNX attention memory on long-context models.
    max_dense_embed_tokens: int = Field(default=0)
    # Max tokens fed to the sparse encoder. 0 = no limit (default for Qdrant/bm25).
    max_sparse_embed_tokens: int = Field(default=0)
    # ONNX intra-op threads for the dense encoder. 0 = auto-detect from CPU count
    # (or OMP_NUM_THREADS). Sparse encoder threads are required via SPARSE_THREADS.
    dense_threads: int = Field(default=0)

    # Force sequential (sparse then dense) embedding instead of concurrent.
    # Trades ~40-50% throughput for ~50% lower peak memory during indexing.
    # Default false: concurrent is safe when BATCH_SIZE is properly tuned.
    sequential_embed: bool = Field(default=False)

    # --- Memory pressure thresholds (cgroup-aware OOM prevention) ---
    # At warn_pct: halve ONNX batch size, disable dense/sparse concurrency.
    # At halt_pct: abort the current indexing flush and log an actionable error.
    memory_pressure_warn_pct: int = Field(default=70)
    memory_pressure_halt_pct: int = Field(default=85)

    # --- Qdrant storage tuning (affects RAM vs search speed) ---
    # Store dense vectors on disk (memory-mapped) instead of fully in RAM.
    vectors_on_disk: bool = Field(default=True)
    # Store the sparse vector index on disk.
    sparse_on_disk: bool = Field(default=True)
    # Enable int8 scalar quantization of dense vectors (~4x less vector RAM,
    # rescoring preserves quality). Disable on RAM-rich hosts for max speed.
    quantization: bool = Field(default=True)
    # Segments larger than this many KB are memory-mapped rather than kept in
    # RAM. Lower = less RAM, higher = faster.
    memmap_threshold_kb: int = Field(default=20000)
    # Create keyword payload indexes (rel_path, chunk_id, symbol_name, language)
    # so filtered lookups/deletes don't do full payload scans. Toggleable so the
    # benchmark harness can measure the indexes-off vs indexes-on delta.
    payload_indexes: bool = Field(default=True)

    # --- Service-mapping / cross-reference tuning (project-agnostic) ---
    # Comma-separated URL path keywords used to recognise API paths in config
    # and code (e.g. "/api/...", "/rest/..."). Extend for your domain without
    # editing source — these feed the URL extraction regexes.
    service_url_keywords: str = Field(default=DEFAULT_SERVICE_URL_KEYWORDS)
    # Extra natural-language discovery queries for map_service_dependencies.
    # Separate multiple queries with a pipe (|) or newline. Empty by default.
    service_discovery_extra_queries: str = Field(default="")

    @model_validator(mode="after")
    def validate_dense_embed_vector_size_matches_model(self) -> Self:
        expected = KNOWN_EMBED_MODEL_DIMENSIONS.get(self.dense_embed_model)
        if expected is not None and self.dense_embed_vector_size != expected:
            raise ValueError(
                f"DENSE_EMBED_VECTOR_SIZE={self.dense_embed_vector_size} does not match "
                f"DENSE_EMBED_MODEL={self.dense_embed_model!r} "
                f"(expected {expected})."
            )
        return self

    @property
    def excluded_dirs_set(self) -> set[str]:
        return {d.strip() for d in self.excluded_dirs.split(",") if d.strip()}

    @property
    def service_url_keyword_list(self) -> list[str]:
        return [k.strip() for k in self.service_url_keywords.split(",") if k.strip()]

    @property
    def service_discovery_extra_query_list(self) -> list[str]:
        raw = self.service_discovery_extra_queries.replace("|", "\n")
        return [q.strip() for q in raw.splitlines() if q.strip()]
