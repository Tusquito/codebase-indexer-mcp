"""ColBERT multivector embedding via HTTP sidecar."""

from __future__ import annotations

import asyncio
import logging
import time
from typing import Any

import httpx

from codebase_indexer.config import KNOWN_COLBERT_TOKEN_DIMENSIONS
from codebase_indexer.indexer.backends.base import EmbeddingError

_tlog = logging.getLogger(__name__)


class ColbertRemoteBackend:
    """Late-interaction encoder that delegates to colbert_worker HTTP API."""

    backend_name = "remote_colbert"

    def __init__(
        self,
        *,
        model_name: str,
        colbert_url: str = "http://colbert_worker:8082",
        batch_size: int = 16,
        timeout: float = 300.0,
        max_retries: int = 3,
    ) -> None:
        self.model_name = model_name
        self.colbert_url = colbert_url.rstrip("/")
        self.batch_size = batch_size
        self.timeout = timeout
        self.max_retries = max_retries
        expected = KNOWN_COLBERT_TOKEN_DIMENSIONS.get(model_name)
        self.token_dimension = expected if expected is not None else 128
        self._async_client: httpx.AsyncClient | None = None
        self._ready = False

    def is_loaded(self) -> bool:
        return self._ready

    def _get_async_client(self) -> httpx.AsyncClient:
        if self._async_client is None:
            self._async_client = httpx.AsyncClient(
                base_url=self.colbert_url,
                timeout=httpx.Timeout(self.timeout),
            )
        return self._async_client

    def preload(self) -> None:
        with httpx.Client(
            base_url=self.colbert_url,
            timeout=httpx.Timeout(self.timeout),
        ) as client:
            try:
                health_resp = client.get("/health")
                health_resp.raise_for_status()
                health = health_resp.json()
                if health.get("model") and health["model"] != self.model_name:
                    _tlog.warning(
                        "colbert_sidecar_model_mismatch expected=%s sidecar=%s",
                        self.model_name,
                        health.get("model"),
                    )
                sidecar_dim = health.get("token_dimension")
                if sidecar_dim is not None and sidecar_dim != self.token_dimension:
                    raise EmbeddingError(
                        f"ColBERT sidecar token_dimension {sidecar_dim} "
                        f"does not match expected {self.token_dimension} "
                        f"for model {self.model_name!r}"
                    )
                probe_resp = client.post(
                    "/v1/embed/colbert",
                    json={"texts": ["."]},
                )
                probe_resp.raise_for_status()
                data = probe_resp.json()
                embeddings = data.get("embeddings") or []
                token_dim = data.get("token_dimension", self.token_dimension)
                if not embeddings:
                    raise EmbeddingError(
                        "ColBERT sidecar probe returned no embeddings"
                    )
                if token_dim != self.token_dimension:
                    raise EmbeddingError(
                        f"ColBERT sidecar token_dimension {token_dim} "
                        f"does not match expected {self.token_dimension}"
                    )
                if embeddings and embeddings[0] and len(embeddings[0][0]) != self.token_dimension:
                    raise EmbeddingError(
                        f"ColBERT sidecar embedding dimension mismatch: "
                        f"expected {self.token_dimension}, "
                        f"got {len(embeddings[0][0])}"
                    )
                self._ready = True
                _tlog.info(
                    "colbert_remote_ready model=%s url=%s",
                    self.model_name,
                    self.colbert_url,
                )
            except httpx.HTTPError as exc:
                raise EmbeddingError(
                    f"ColBERT sidecar preload failed at {self.colbert_url}: {exc}"
                ) from exc

    def release(self) -> None:
        self._ready = False

    async def embed_batch(self, texts: list[str]) -> list[list[list[float]]]:
        if not texts:
            return []
        results: list[list[list[float]]] = []
        for i in range(0, len(texts), self.batch_size):
            batch = texts[i : i + self.batch_size]
            results.extend(await self._embed_http(batch))
        for idx, multivec in enumerate(results):
            if multivec and len(multivec[0]) != self.token_dimension:
                raise EmbeddingError(
                    f"ColBERT embedding {idx} token dimension mismatch: "
                    f"expected {self.token_dimension}, got {len(multivec[0])}"
                )
        return results

    async def _embed_http(self, texts: list[str]) -> list[list[list[float]]]:
        client = self._get_async_client()
        payload: dict[str, Any] = {"texts": texts}
        last_exc: Exception | None = None
        for attempt in range(self.max_retries):
            try:
                t0 = time.monotonic()
                resp = await client.post("/v1/embed/colbert", json=payload)
                if resp.status_code == 503 and attempt < self.max_retries - 1:
                    await asyncio.sleep(2**attempt)
                    continue
                resp.raise_for_status()
                data = resp.json()
                embeddings = data.get("embeddings")
                if not embeddings or len(embeddings) != len(texts):
                    raise EmbeddingError(
                        f"ColBERT sidecar returned {len(embeddings or [])} embeddings "
                        f"for {len(texts)} inputs"
                    )
                token_dim = data.get("token_dimension", self.token_dimension)
                if token_dim != self.token_dimension:
                    raise EmbeddingError(
                        f"ColBERT sidecar token_dimension {token_dim} "
                        f"does not match expected {self.token_dimension}"
                    )
                _tlog.info(
                    "colbert_remote_embed_done chunks=%d elapsed_s=%.2f",
                    len(texts),
                    time.monotonic() - t0,
                )
                return embeddings
            except (httpx.HTTPError, EmbeddingError) as exc:
                last_exc = exc
                if attempt < self.max_retries - 1:
                    await asyncio.sleep(2**attempt)
                    continue
                raise EmbeddingError(f"ColBERT sidecar embed failed: {exc}") from exc
        raise EmbeddingError(f"ColBERT sidecar embed failed after retries: {last_exc}")
