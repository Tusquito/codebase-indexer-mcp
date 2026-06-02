# src/codebase_indexer/config.py
from pydantic_settings import BaseSettings
from pydantic import Field


class Settings(BaseSettings):
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
    workspace_path: str = Field(default="/workspace")
    log_level: str = Field(default="INFO")

    class Config:
        env_file = ".env"
        case_sensitive = False
