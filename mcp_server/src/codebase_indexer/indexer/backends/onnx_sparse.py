"""Sparse embedding via fastembed BM25."""

from __future__ import annotations

import asyncio
import logging
import time
from typing import Any

from codebase_indexer.config import KNOWN_EMBED_MODEL_MAX_TOKENS
from codebase_indexer.indexer.backends.base import SparseVector, trim_memory
from codebase_indexer.indexer.backends.onnx_common import (
    extract_model_dir,
    extract_tokenizer,
    resolve_threads,
)
from codebase_indexer.indexer.truncation import (
    TruncationSource,
    resolve_max_embed_tokens,
    truncate_for_embedding,
)

_tlog = logging.getLogger(__name__)


class OnnxSparseBackend:
    """fastembed sparse encoder (BM25 by default)."""

    backend_name = "onnx"

    _shared_model = None
    _shared_tokenizer: Any | None = None
    _shared_max_tokens: int = 0
    _shared_truncation_source: TruncationSource = "disabled"

    def __init__(
        self,
        *,
        model_name: str,
        sparse_threads: int = 0,
        max_sparse_embed_tokens: int = 0,
    ) -> None:
        self.model_name = model_name
        self.sparse_threads = sparse_threads
        self._max_sparse_embed_tokens_cfg = max_sparse_embed_tokens
        self._truncation_ready = False

    @classmethod
    def release_shared(cls) -> None:
        cls._shared_model = None
        trim_memory()

    def is_loaded(self) -> bool:
        return self._shared_model is not None

    def preload(self) -> None:
        self._get_model()

    def release(self) -> None:
        type(self).release_shared()

    async def embed_batch(self, texts: list[str]) -> list[SparseVector]:
        loop = asyncio.get_running_loop()
        return await loop.run_in_executor(None, self._embed_batch_sync, texts)

    @property
    def _max_tokens(self) -> int:
        return type(self)._shared_max_tokens

    def _ensure_truncation(self) -> None:
        if self._truncation_ready:
            return
        model = self._get_model()
        cls = type(self)
        cls._shared_tokenizer = extract_tokenizer(model)
        model_dir = extract_model_dir(model)
        max_tok, source = resolve_max_embed_tokens(
            role="sparse",
            model_name=self.model_name,
            env_tokens=self._max_sparse_embed_tokens_cfg,
            model_dir=model_dir,
            known_registry=KNOWN_EMBED_MODEL_MAX_TOKENS,
        )
        cls._shared_max_tokens = max_tok
        cls._shared_truncation_source = source
        self._truncation_ready = True

    def _get_model(self):
        cls = type(self)
        if cls._shared_model is None:
            from fastembed.sparse import SparseTextEmbedding

            threads = resolve_threads(self.sparse_threads)
            _tlog.info(
                "loading_sparse_model model=%s threads=%d", self.model_name, threads
            )
            t0 = time.monotonic()
            cls._shared_model = SparseTextEmbedding(
                model_name=self.model_name, threads=threads
            )
            _tlog.info(
                "sparse_model_loaded model=%s elapsed_s=%.2f",
                self.model_name,
                time.monotonic() - t0,
            )
        return cls._shared_model

    def _truncate(self, text: str) -> str:
        self._ensure_truncation()
        if self._max_tokens <= 0:
            return text
        truncated, _ = truncate_for_embedding(
            text,
            max_tokens=self._max_tokens,
            tokenizer=type(self)._shared_tokenizer,
        )
        return truncated

    def _embed_batch_sync(self, texts: list[str]) -> list[SparseVector]:
        model = self._get_model()
        self._ensure_truncation()
        cls = type(self)
        if self._max_tokens > 0:
            truncated = [self._truncate(t) for t in texts]
            truncated_count = sum(
                1 for orig, tr in zip(texts, truncated) if len(orig) != len(tr)
            )
            if truncated_count:
                _tlog.warning(
                    "sparse_chunks_truncated count=%d max_tokens=%d source=%s",
                    truncated_count,
                    self._max_tokens,
                    cls._shared_truncation_source,
                )
            texts = truncated
        _tlog.info("sparse_embed_start chunks=%d", len(texts))
        t0 = time.monotonic()
        result = [
            SparseVector(indices=r.indices.tolist(), values=r.values.tolist())
            for r in model.embed(texts)
        ]
        _tlog.info(
            "sparse_embed_done chunks=%d elapsed_s=%.2f",
            len(result),
            time.monotonic() - t0,
        )
        return result

    def _get_sparse_model(self):
        return self._get_model()

    def _embed_sparse_batch_sync(self, texts: list[str]) -> list[SparseVector]:
        return self._embed_batch_sync(texts)
