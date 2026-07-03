"""Settings for the ColBERT HTTP sidecar process."""

from pydantic import Field, field_validator
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
    colbert_use_cuda: bool = Field(default=False)
    colbert_device_ids: list[int] | None = Field(default=None)
    host: str = Field(default="0.0.0.0")
    port: int = Field(default=8082)

    @field_validator("colbert_device_ids", mode="before")
    @classmethod
    def _parse_device_ids(cls, value: object) -> list[int] | None:
        if value is None or value == "":
            return None
        if isinstance(value, list):
            return value
        if isinstance(value, str):
            parts = [part.strip() for part in value.split(",") if part.strip()]
            return [int(part) for part in parts] if parts else None
        return value
