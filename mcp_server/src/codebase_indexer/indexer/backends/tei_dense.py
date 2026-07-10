"""Dense embedding via HuggingFace TEI OpenAI-compatible HTTP API."""

from __future__ import annotations

import asyncio
import logging
import math
import time
from typing import Any

import httpx

from codebase_indexer.indexer.backends.base import EmbeddingError
from codebase_indexer.indexer.tokenizer_loader import load_dense_tokenizer
from codebase_indexer.indexer.truncation import (
    resolve_max_embed_tokens,
    truncate_for_embedding,
)
from codebase_indexer.telemetry.metrics import record_embed_request, record_truncated_chunks

_tlog = logging.getLogger(__name__)


class TeiDenseBackend:
    """Dense encoder that delegates to TEI POST /v1/embeddings."""

    backend_name = "tei"

    _shared_tokenizer: Any | None = None
    _tokenizer_load_attempted: bool = False

    def __init__(
        self,
        *,
        model_name: str,
        vector_size: int,
        tei_url: str = "http://tei:80",
        batch_size: int = 32,
        timeout: float = 120.0,
        max_retries: int = 3,
        max_dense_embed_tokens: int = 0,
        dense_embed_model: str = "",
        known_max_tokens: dict[str, int] | None = None,
        mrl_dimensions: int | None = None,
        query_instruction: str = "",
        normalize_output: bool = False,
    ) -> None:
        self.model_name = model_name
        self.vector_size = vector_size
        self._mrl_dimensions = mrl_dimensions
        # ADR 0026 Phase 3 spike hooks. Both default OFF: the Jina/default path
        # is byte-for-byte unchanged unless a candidate opts in.
        #   query_instruction  instruction-tuned retrievers (e.g. inf-retriever)
        #                      need a task prefix on the *query* side only.
        #   normalize_output   candidates whose TEI build emits unnormalized
        #                      vectors (e.g. pplx INT8) need L2 normalization for
        #                      cosine similarity to behave.
        self._query_instruction = query_instruction
        self._normalize_output = normalize_output
        self.tei_url = tei_url.rstrip("/")
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
                base_url=self.tei_url,
                timeout=httpx.Timeout(self.timeout),
            )
        return self._async_client

    def preload(self) -> None:
        with httpx.Client(
            base_url=self.tei_url,
            timeout=httpx.Timeout(self.timeout),
        ) as client:
            try:
                health_resp = client.get("/health")
                health_resp.raise_for_status()
                probe_resp = client.post(
                    "/v1/embeddings",
                    json=self._embed_payload(["."]),
                )
                probe_resp.raise_for_status()
                data = probe_resp.json()
                embedding = self._parse_embedding_response(data, expected_count=1)[0]
                if len(embedding) != self.vector_size:
                    raise EmbeddingError(
                        f"TEI model {self.model_name!r} returned dimension "
                        f"{len(embedding)}, expected {self.vector_size}"
                    )
                self._ready = True
                self._ensure_truncation()
                _tlog.info(
                    "tei_embed_ready model=%s url=%s",
                    self.model_name,
                    self.tei_url,
                )
            except httpx.HTTPError as exc:
                raise EmbeddingError(
                    f"TEI preload failed at {self.tei_url}: {exc}"
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
                "tei_dense_truncation_disabled model=%s reason=tokenizer_unavailable",
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
            record_truncated_chunks("tei", truncated_count)
            _tlog.info(
                "tei_dense_chunks_truncated count=%d max_tokens=%d source=%s model=%s",
                truncated_count,
                self._max_tokens,
                self._truncation_source,
                self._dense_embed_model or self.model_name,
            )
        return truncated

    @staticmethod
    def _l2_normalize(vec: list[float]) -> list[float]:
        norm = math.sqrt(sum(v * v for v in vec))
        if norm == 0.0:
            return vec
        return [v / norm for v in vec]

    def _postprocess(self, results: list[list[float]]) -> list[list[float]]:
        for idx, vec in enumerate(results):
            if len(vec) != self.vector_size:
                raise EmbeddingError(
                    f"TEI embedding {idx} dimension mismatch: "
                    f"expected {self.vector_size}, got {len(vec)}"
                )
        if self._normalize_output:
            results = [self._l2_normalize(vec) for vec in results]
        return results

    async def embed_batch(self, texts: list[str]) -> list[list[float]]:
        if not texts:
            return []
        texts = self._truncate_batch(texts)
        results: list[list[float]] = []
        for i in range(0, len(texts), self.batch_size):
            batch = texts[i : i + self.batch_size]
            results.extend(await self._embed_http(batch))
        return self._postprocess(results)

    async def embed_query(self, texts: list[str]) -> list[list[float]]:
        """Embed query text, applying the candidate's instruction prefix.

        Instruction-tuned retrievers (ADR 0026 spike: ``inf-retriever``) prefix
        only the *query* side. Falls back to :meth:`embed_batch` (no prefix) when
        ``query_instruction`` is empty, so the default path is unchanged.
        """
        if not self._query_instruction:
            return await self.embed_batch(texts)
        prefixed = [f"{self._query_instruction}{text}" for text in texts]
        return await self.embed_batch(prefixed)

    def _embed_payload(self, texts: list[str]) -> dict[str, Any]:
        payload: dict[str, Any] = {
            "model": self.model_name,
            "input": texts if len(texts) > 1 else texts[0],
        }
        if self._mrl_dimensions is not None:
            payload["dimensions"] = self._mrl_dimensions
        return payload

    def _parse_embedding_response(
        self, data: dict[str, Any], *, expected_count: int
    ) -> list[list[float]]:
        items = data.get("data")
        if not items:
            raise EmbeddingError("TEI returned no embedding data")
        items = sorted(items, key=lambda row: row.get("index", 0))
        embeddings = [row["embedding"] for row in items]
        if len(embeddings) != expected_count:
            raise EmbeddingError(
                f"TEI returned {len(embeddings)} embeddings for {expected_count} inputs"
            )
        return embeddings

    async def _embed_http(self, texts: list[str]) -> list[list[float]]:
        client = self._get_async_client()
        payload = self._embed_payload(texts)
        last_exc: Exception | None = None
        for attempt in range(self.max_retries):
            try:
                t0 = time.monotonic()
                resp = await client.post("/v1/embeddings", json=payload)
                if resp.status_code == 503 and attempt < self.max_retries - 1:
                    await asyncio.sleep(2**attempt)
                    continue
                resp.raise_for_status()
                data = resp.json()
                embeddings = self._parse_embedding_response(
                    data, expected_count=len(texts)
                )
                _tlog.info(
                    "tei_embed_done chunks=%d elapsed_s=%.2f",
                    len(texts),
                    time.monotonic() - t0,
                )
                record_embed_request("tei", "success")
                return embeddings
            except (httpx.HTTPError, EmbeddingError) as exc:
                last_exc = exc
                if attempt < self.max_retries - 1:
                    await asyncio.sleep(2**attempt)
                    continue
                record_embed_request("tei", "error")
                raise EmbeddingError(f"TEI embed failed: {exc}") from exc
        record_embed_request("tei", "error")
        raise EmbeddingError(f"TEI embed failed after retries: {last_exc}")
