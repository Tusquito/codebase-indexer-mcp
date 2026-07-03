"""Dense embedding via Ollama HTTP API."""

from __future__ import annotations

import asyncio
import logging
import time
from typing import Any

import httpx

from codebase_indexer.indexer.backends.base import EmbeddingError
from codebase_indexer.indexer.tokenizer_loader import load_dense_tokenizer
from codebase_indexer.indexer.truncation import (
    resolve_max_embed_tokens,
    truncate_for_embedding,
)

_tlog = logging.getLogger(__name__)


class OllamaDenseBackend:
    """Dense encoder that delegates to Ollama /api/embed."""

    backend_name = "ollama"

    _shared_tokenizer: Any | None = None
    _tokenizer_load_attempted: bool = False

    def __init__(
        self,
        *,
        model_name: str,
        vector_size: int,
        ollama_url: str = "http://host.docker.internal:11434",
        batch_size: int = 32,
        timeout: float = 120.0,
        max_retries: int = 3,
        max_dense_embed_tokens: int = 0,
        dense_embed_model: str = "",
        known_max_tokens: dict[str, int] | None = None,
    ) -> None:
        self.model_name = model_name
        self.vector_size = vector_size
        self.ollama_url = ollama_url.rstrip("/")
        self.batch_size = batch_size
        self.timeout = timeout
        self.max_retries = max_retries
        self._dense_embed_model = dense_embed_model
        self._known_max_tokens = known_max_tokens or {}
        self._max_tokens, self._truncation_source = resolve_max_embed_tokens(
            role="dense",
            model_name=dense_embed_model or model_name,
            env_tokens=max_dense_embed_tokens,
            model_dir=None,
            known_registry=self._known_max_tokens,
        )
        self._async_client: httpx.AsyncClient | None = None
        self._ready = False

    def is_loaded(self) -> bool:
        return self._ready

    def _get_async_client(self) -> httpx.AsyncClient:
        if self._async_client is None:
            self._async_client = httpx.AsyncClient(
                base_url=self.ollama_url,
                timeout=httpx.Timeout(self.timeout),
            )
        return self._async_client

    def preload(self) -> None:
        with httpx.Client(
            base_url=self.ollama_url,
            timeout=httpx.Timeout(self.timeout),
        ) as client:
            try:
                resp = client.get("/api/tags")
                resp.raise_for_status()
                tags = resp.json().get("models", [])
                names = {m.get("name", "").split(":")[0] for m in tags}
                model_base = self.model_name.split(":")[0]
                if names and model_base not in names and self.model_name not in {
                    m.get("name", "") for m in tags
                }:
                    _tlog.warning(
                        "ollama_model_not_found model=%s available=%s",
                        self.model_name,
                        sorted(names),
                    )
                probe_resp = client.post(
                    "/api/embed",
                    json={"model": self.model_name, "input": ["."]},
                )
                probe_resp.raise_for_status()
                data = probe_resp.json()
                embeddings = data.get("embeddings") or [data.get("embedding")]
                if not embeddings or len(embeddings[0]) != self.vector_size:
                    raise EmbeddingError(
                        f"Ollama model {self.model_name!r} returned dimension "
                        f"{len(embeddings[0]) if embeddings else 0}, "
                        f"expected {self.vector_size}"
                    )
                self._ready = True
                self._ensure_truncation()
                _tlog.info(
                    "ollama_embed_ready model=%s url=%s",
                    self.model_name,
                    self.ollama_url,
                )
            except httpx.HTTPError as exc:
                raise EmbeddingError(
                    f"Ollama preload failed at {self.ollama_url}: {exc}"
                ) from exc

    def release(self) -> None:
        self._ready = False

    def _ensure_truncation(self) -> None:
        cls = type(self)
        if cls._tokenizer_load_attempted:
            return
        cls._tokenizer_load_attempted = True
        model_id = self._dense_embed_model or self.model_name
        cls._shared_tokenizer = load_dense_tokenizer(model_id)
        if cls._shared_tokenizer is None:
            _tlog.warning(
                "ollama_dense_truncation_disabled model=%s reason=tokenizer_unavailable",
                model_id,
            )

    def _truncate_batch(self, texts: list[str]) -> list[str]:
        if self._max_tokens <= 0:
            return texts
        self._ensure_truncation()
        truncated: list[str] = []
        truncated_count = 0
        for text in texts:
            new_text, _ = truncate_for_embedding(
                text,
                max_tokens=self._max_tokens,
                tokenizer=type(self)._shared_tokenizer,
            )
            if len(new_text) != len(text):
                truncated_count += 1
            truncated.append(new_text)
        if truncated_count:
            _tlog.info(
                "ollama_dense_chunks_truncated count=%d max_tokens=%d source=%s model=%s",
                truncated_count,
                self._max_tokens,
                self._truncation_source,
                self._dense_embed_model or self.model_name,
            )
        return truncated

    async def embed_batch(self, texts: list[str]) -> list[list[float]]:
        if not texts:
            return []
        texts = self._truncate_batch(texts)
        results: list[list[float]] = []
        for i in range(0, len(texts), self.batch_size):
            batch = texts[i : i + self.batch_size]
            results.extend(await self._embed_http(batch))
        for idx, vec in enumerate(results):
            if len(vec) != self.vector_size:
                raise EmbeddingError(
                    f"Ollama embedding {idx} dimension mismatch: "
                    f"expected {self.vector_size}, got {len(vec)}"
                )
        return results

    async def _embed_http(self, texts: list[str]) -> list[list[float]]:
        client = self._get_async_client()
        payload: dict[str, Any] = {"model": self.model_name, "input": texts}
        last_exc: Exception | None = None
        for attempt in range(self.max_retries):
            try:
                t0 = time.monotonic()
                resp = await client.post("/api/embed", json=payload)
                if resp.status_code == 503 and attempt < self.max_retries - 1:
                    await asyncio.sleep(2**attempt)
                    continue
                resp.raise_for_status()
                data = resp.json()
                embeddings = data.get("embeddings")
                if embeddings is None and "embedding" in data:
                    embeddings = [data["embedding"]]
                if not embeddings or len(embeddings) != len(texts):
                    raise EmbeddingError(
                        f"Ollama returned {len(embeddings or [])} embeddings "
                        f"for {len(texts)} inputs"
                    )
                _tlog.info(
                    "ollama_embed_done chunks=%d elapsed_s=%.2f",
                    len(texts),
                    time.monotonic() - t0,
                )
                return embeddings
            except (httpx.HTTPError, EmbeddingError) as exc:
                last_exc = exc
                if attempt < self.max_retries - 1:
                    await asyncio.sleep(2**attempt)
                    continue
                raise EmbeddingError(f"Ollama embed failed: {exc}") from exc
        raise EmbeddingError(f"Ollama embed failed after retries: {last_exc}")
