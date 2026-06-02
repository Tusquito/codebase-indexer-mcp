# src/codebase_indexer/config.py
from pydantic_settings import BaseSettings, SettingsConfigDict
from pydantic import Field


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=".env",
        case_sensitive=False,
        extra="ignore",
    )

    qdrant_url: str = Field(default="http://localhost:6333")
    qdrant_collection: str = Field(default="codebase")
    embed_model: str = Field(default="nomic-ai/nomic-embed-text-v1.5")
    vector_size: int = Field(default=768)
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

    # --- Pipeline tuning knobs (hardware-portable; all env-overridable) ---
    # Number of chunks accumulated before an embed+upsert flush. The double
    # buffer keeps at most 2 of these in memory, so peak RAM scales with this.
    flush_every: int = Field(default=1500)
    # Points per Qdrant upsert sub-batch (stays within gRPC/HTTP size limits).
    upsert_batch: int = Field(default=500)
    # How many scanned files may be queued ahead of the consumer.
    readahead_buffer: int = Field(default=100)
    # Hard cap on characters fed to the dense encoder (ONNX attention is
    # O(seq_len^2 * batch); this bounds peak embedding memory).
    max_embed_chars: int = Field(default=4096)
    # ONNX intra-op threads for dense / sparse encoders. 0 = auto-detect from
    # CPU count (leaves headroom for Qdrant + the asyncio loop). Set explicitly
    # on bigger machines to scale throughput.
    dense_threads: int = Field(default=0)
    sparse_threads: int = Field(default=0)

    # --- Qdrant storage tuning (affects RAM vs search speed) ---
    # Store dense vectors on disk (memory-mapped) instead of fully in RAM.
    vectors_on_disk: bool = Field(default=True)
    # Store the sparse (BM25) index on disk.
    sparse_on_disk: bool = Field(default=True)
    # Enable int8 scalar quantization of dense vectors (~4x less vector RAM,
    # rescoring preserves quality). Disable on RAM-rich hosts for max speed.
    quantization: bool = Field(default=True)
    # Segments larger than this many KB are memory-mapped rather than kept in
    # RAM. Lower = less RAM, higher = faster.
    memmap_threshold_kb: int = Field(default=20000)

    # --- Service-mapping / cross-reference tuning (project-agnostic) ---
    # Comma-separated URL path keywords used to recognise API paths in config
    # and code (e.g. "/api/...", "/rest/..."). Extend for your domain without
    # editing source — these feed the URL extraction regexes.
    service_url_keywords: str = Field(
        default="rest,api,profile,service,internal,public,gateway,graphql,webhook,auth,users,accounts"
    )
    # Extra natural-language discovery queries for map_service_dependencies.
    # Separate multiple queries with a pipe (|) or newline. Empty by default.
    service_discovery_extra_queries: str = Field(default="")

    @property
    def service_url_keyword_list(self) -> list[str]:
        return [k.strip() for k in self.service_url_keywords.split(",") if k.strip()]

    @property
    def service_discovery_extra_query_list(self) -> list[str]:
        raw = self.service_discovery_extra_queries.replace("|", "\n")
        return [q.strip() for q in raw.splitlines() if q.strip()]
