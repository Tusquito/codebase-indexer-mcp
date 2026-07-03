"""Settings for the ColBERT HTTP sidecar process."""

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class WorkerSettings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=".env",
        case_sensitive=False,
        extra="ignore",
    )

    colbert_embed_model: str = Field(default="colbert-ir/colbertv2.0")
    sparse_threads: int = Field(default=0)
    rerank_max_query_tokens: int = Field(default=0)
    host: str = Field(default="0.0.0.0")
    port: int = Field(default=8082)
