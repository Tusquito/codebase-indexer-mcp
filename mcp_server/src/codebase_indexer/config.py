# src/codebase_indexer/config.py
from typing import Literal, Self

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

# Jina Embeddings v2 base code — code search (dimension, max sequence length in tokens).
# https://huggingface.co/jinaai/jina-embeddings-v2-base-code
JINA_CODE_EMBED_V2_SPECS: dict[str, tuple[int, int]] = {
    "jinaai/jina-embeddings-v2-base-code": (768, 8192),
}

# Qwen3 Embedding — Matryoshka (MRL) models via Ollama (native_dim, max_tokens).
# https://huggingface.co/Qwen/Qwen3-Embedding-4B
QWEN3_EMBED_SPECS: dict[str, tuple[int, int]] = {
    "Qwen/Qwen3-Embedding-0.6B": (1024, 32768),
    "Qwen/Qwen3-Embedding-4B": (2560, 32768),
    "Qwen/Qwen3-Embedding-8B": (4096, 32768),
}

QWEN3_MRL_MIN_DIMENSIONS = 32

# Known dense embedding models and their output dimensions. When listed,
# DENSE_EMBED_VECTOR_SIZE must match exactly (set both in .env — see .env.example).
# Qwen3 models use MRL validation instead (32 <= size <= native).
KNOWN_EMBED_MODEL_DIMENSIONS: dict[str, int] = {
    "nomic-ai/nomic-embed-text-v1.5": 768,
    **{model: dims for model, (dims, _) in BGE_EN_V1_5_SPECS.items()},
    **{model: dims for model, (dims, _) in JINA_CODE_EMBED_V2_SPECS.items()},
    **{model: dims for model, (dims, _) in QWEN3_EMBED_SPECS.items()},
}

# Known dense transformer models and max input tokens (embedding truncation).
KNOWN_EMBED_MODEL_MAX_TOKENS: dict[str, int] = {
    "nomic-ai/nomic-embed-text-v1.5": 8192,
    **{model: max_tokens for model, (_, max_tokens) in BGE_EN_V1_5_SPECS.items()},
    **{model: max_tokens for model, (_, max_tokens) in JINA_CODE_EMBED_V2_SPECS.items()},
    **{model: max_tokens for model, (_, max_tokens) in QWEN3_EMBED_SPECS.items()},
}


def qwen3_native_dimensions(model_id: str) -> int | None:
    spec = QWEN3_EMBED_SPECS.get(model_id)
    return spec[0] if spec else None


def ollama_embed_dimensions(dense_embed_model: str, vector_size: int) -> int | None:
    """Return Ollama MRL ``dimensions`` when ``vector_size`` is below native."""
    native = qwen3_native_dimensions(dense_embed_model)
    if native is None or vector_size >= native:
        return None
    return vector_size


def validate_qwen3_mrl_vector_size(model_id: str, vector_size: int) -> None:
    native = qwen3_native_dimensions(model_id)
    if native is None:
        return
    if vector_size < QWEN3_MRL_MIN_DIMENSIONS or vector_size > native:
        raise ValueError(
            f"DENSE_EMBED_VECTOR_SIZE={vector_size} invalid for "
            f"DENSE_EMBED_MODEL={model_id!r} "
            f"(MRL: {QWEN3_MRL_MIN_DIMENSIONS}..{native})."
        )

# ColBERT late-interaction models (token dimension, max sequence length in tokens).
# https://huggingface.co/colbert-ir/colbertv2.0
COLBERT_EMBED_SPECS: dict[str, tuple[int, int]] = {
    "colbert-ir/colbertv2.0": (128, 512),
}

KNOWN_COLBERT_TOKEN_DIMENSIONS: dict[str, int] = {
    model: token_dim for model, (token_dim, _) in COLBERT_EMBED_SPECS.items()
}

KNOWN_COLBERT_MODEL_MAX_TOKENS: dict[str, int] = {
    model: max_tokens for model, (_, max_tokens) in COLBERT_EMBED_SPECS.items()
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
    # Dense vectors always come from Ollama HTTP (see docker-compose.ollama.yml).
    dense_embed_backend: Literal["ollama"] = Field(default="ollama")
    ollama_url: str = Field(default="http://host.docker.internal:11434")
    ollama_embed_model: str = Field(default="")
    ollama_embed_batch_size: int = Field(default=32)
    ollama_timeout: int = Field(default=120)
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

    # Application Prometheus metrics at GET /metrics (ADR 0018 Phase 1).
    metrics_enabled: bool = Field(default=False)

    # Directory names pruned during the scan walk (build artifacts, VCS, caches,
    # migration history, editor metadata). Comma-separated and env-overridable.
    # Project-specific or
    # content-bearing folders (e.g. docs) belong in a per-project
    # .codeindexignore rather than this global default.
    excluded_dirs: str = Field(
        default=(
            "node_modules,.git,__pycache__,.venv,venv,dist,build,target,bin,obj,"
            ".gradle,.mypy_cache,.pytest_cache,.ruff_cache,.github,.idea,.vscode,"
            "migrations"
        )
    )

    # Release sparse ONNX model after indexing completes. Ollama dense has no
    # in-process model to release; sparse BM25 reloads from fastembed_cache.
    # Default on: indexing is infrequent, idle RAM costs more than reload latency.
    # Set to false only if you need sub-second first-search latency after indexing.
    release_models_after_index: bool = Field(default=True)

    # Seconds of embed inactivity before sparse ONNX is automatically released.
    # Covers cases where models were loaded for search but the server goes idle.
    # 0 disables the idle timer (models stay until process restart or explicit release).
    model_idle_timeout: int = Field(default=300)

    # Eagerly probe Ollama and load sparse BM25 during startup (default: true).
    preload_models: bool = Field(default=True)

    # --- Pipeline tuning knobs (hardware-portable; all env-overridable) ---
    # Number of chunks accumulated before an embed+upsert flush. The double
    # buffer keeps at most 2 of these in memory, so peak RAM scales with this.
    flush_every: int = Field(default=1500)
    # Points per Qdrant upsert sub-batch (stays within gRPC/HTTP size limits).
    upsert_batch: int = Field(default=500)
    # How many scanned files may be queued ahead of the consumer.
    readahead_buffer: int = Field(default=100)
    # Max tokens sent to Ollama before /api/embed (model tokenizer from DENSE_EMBED_MODEL).
    # 0 = auto-detect from DENSE_EMBED_MODEL registry (e.g. 8192 for Jina code).
    max_dense_embed_tokens: int = Field(default=0)
    # Max tokens fed to the sparse encoder. 0 = no limit (default for Qdrant/bm25).
    max_sparse_embed_tokens: int = Field(default=0)

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

    # --- Qdrant search/HNSW tuning (recall vs latency) ---
    # Oversampling factor for int8 quantized search: fetch this many more
    # candidates on the quantized index, then rescore against full vectors to
    # recover recall. Only applied when quantization=True.
    quant_oversampling: float = Field(default=2.0, gt=0)
    # Query-time HNSW search breadth (ef). Higher = better recall, slower.
    hnsw_ef: int = Field(default=64, ge=1)
    # HNSW graph degree (m) at build time. Higher = better recall, more RAM.
    hnsw_m: int = Field(default=16, ge=1)
    # HNSW construction search breadth (ef_construct). Higher = better graph,
    # slower index build.
    hnsw_ef_construct: int = Field(default=128, ge=1)
    # Hybrid prefetch limit multiplier: each dense/sparse prefetch fetches
    # top_k * this many candidates before RRF fusion. Higher = better fusion
    # recall, slower.
    prefetch_multiplier: int = Field(default=5, ge=1)
    # RRF constant used when re-fusing ranked results across collections.
    rrf_k: int = Field(default=60, ge=1)

    # --- Optional ColBERT late-interaction reranking (ADR 0008) ---
    # Master switch; requires HYBRID_SEARCH=true and a full re-index to populate
    # multivector ``colbert`` payloads on existing collections.
    rerank_enabled: bool = Field(default=False)
    colbert_embed_model: str = Field(default="colbert-ir/colbertv2.0")
    # Hybrid candidate pool size before ColBERT MAX_SIM rerank (not top_k).
    rerank_prefetch: int = Field(default=100, ge=1)
    # Max tokens for ColBERT query embedding. 0 = model registry default.
    rerank_max_query_tokens: int = Field(default=0)
    # When rerank is on, probe hybrid RRF scores first; skip ColBERT MAX_SIM when
    # rank-1 minus rank-2 gap >= rerank_adaptive_gap (per-collection).
    rerank_adaptive_enabled: bool = Field(default=True)
    rerank_adaptive_gap: float = Field(default=0.02, ge=0)
    colbert_embed_backend: Literal["onnx", "remote"] = Field(default="onnx")
    colbert_url: str = Field(default="http://colbert_worker:8082")
    colbert_timeout: int = Field(default=300)
    colbert_embed_batch_size: int = Field(default=16)

    # --- Recommendation search (ADR 0014) ---
    recommend_enabled: bool = Field(default=True)
    recommend_max_examples: int = Field(default=10, ge=1)
    outlier_max_context_samples: int = Field(default=200, ge=1)
    outlier_max_similarity: float = Field(default=0.55, ge=0.0, le=1.0)

    # --- Service-mapping / cross-reference tuning (project-agnostic) ---
    # Comma-separated URL path keywords used to recognise API paths in config
    # and code (e.g. "/api/...", "/rest/..."). Extend for your domain without
    # editing source — these feed the URL extraction regexes.
    service_url_keywords: str = Field(default=DEFAULT_SERVICE_URL_KEYWORDS)
    # Extra natural-language discovery queries for map_service_dependencies.
    # Separate multiple queries with a pipe (|) or newline. Empty by default.
    service_discovery_extra_queries: str = Field(default="")

    # --- Optional GraphRAG (ADR 0002) ---
    graph_enabled: bool = Field(default=False)
    neo4j_uri: str = Field(default="bolt://neo4j:7687")
    neo4j_user: str = Field(default="neo4j")
    neo4j_password: str = Field(default="")
    neo4j_database: str = Field(default="neo4j")
    graph_writer_batch: int = Field(default=500, ge=1)
    graph_max_hops: int = Field(default=2, ge=1)
    graph_max_nodes: int = Field(default=200, ge=1)

    @model_validator(mode="after")
    def validate_graph_password_when_enabled(self) -> Self:
        if self.graph_enabled and not self.neo4j_password.strip():
            raise ValueError(
                "NEO4J_PASSWORD must be set when GRAPH_ENABLED=true."
            )
        return self

    @model_validator(mode="after")
    def validate_dense_embed_vector_size_matches_model(self) -> Self:
        if self.dense_embed_model in QWEN3_EMBED_SPECS:
            validate_qwen3_mrl_vector_size(
                self.dense_embed_model, self.dense_embed_vector_size
            )
            return self
        expected = KNOWN_EMBED_MODEL_DIMENSIONS.get(self.dense_embed_model)
        if expected is not None and self.dense_embed_vector_size != expected:
            raise ValueError(
                f"DENSE_EMBED_VECTOR_SIZE={self.dense_embed_vector_size} does not match "
                f"DENSE_EMBED_MODEL={self.dense_embed_model!r} "
                f"(expected {expected})."
            )
        return self

    @model_validator(mode="after")
    def validate_dense_embed_backend(self) -> Self:
        if self.dense_embed_backend != "ollama":
            raise ValueError(
                f"DENSE_EMBED_BACKEND must be 'ollama', got {self.dense_embed_backend!r}"
            )
        return self

    @model_validator(mode="after")
    def validate_rerank_requires_hybrid(self) -> Self:
        if self.rerank_enabled and not self.hybrid_search:
            raise ValueError(
                "RERANK_ENABLED=true requires HYBRID_SEARCH=true "
                "(ColBERT rerank runs over hybrid prefetch candidates)."
            )
        return self

    @model_validator(mode="after")
    def validate_adaptive_rerank_when_disabled(self) -> Self:
        if not self.rerank_enabled:
            object.__setattr__(self, "rerank_adaptive_enabled", False)
        return self

    @model_validator(mode="after")
    def default_remote_colbert_when_rerank_enabled(self) -> Self:
        if self.rerank_enabled and "colbert_embed_backend" not in self.model_fields_set:
            object.__setattr__(self, "colbert_embed_backend", "remote")
        return self

    @model_validator(mode="after")
    def validate_colbert_embed_backend(self) -> Self:
        if self.colbert_embed_backend not in ("onnx", "remote"):
            raise ValueError(
                f"COLBERT_EMBED_BACKEND must be 'onnx' or 'remote', "
                f"got {self.colbert_embed_backend!r}"
            )
        if (
            self.rerank_enabled
            and self.colbert_embed_backend == "remote"
            and not self.colbert_url.strip()
        ):
            raise ValueError(
                "COLBERT_URL must be set when RERANK_ENABLED=true and "
                "COLBERT_EMBED_BACKEND=remote"
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
