"""ColBERT late-interaction embedding via fastembed ONNX."""

from __future__ import annotations

import asyncio
import logging
import time
from typing import Any

from codebase_indexer.config import (
    KNOWN_COLBERT_MODEL_MAX_TOKENS,
    KNOWN_COLBERT_TOKEN_DIMENSIONS,
)
from codebase_indexer.indexer.backends.base import trim_memory
from codebase_indexer.indexer.backends.onnx_common import (
    extract_execution_providers,
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


class ColbertOnnxBackend:
    """fastembed ColBERT multivector encoder for reranking."""

    backend_name = "onnx_colbert"

    _shared_model = None
    _shared_tokenizer: Any | None = None
    _shared_max_tokens: int = 0
    _shared_truncation_source: TruncationSource = "disabled"

    def __init__(
        self,
        *,
        model_name: str,
        sparse_threads: int = 0,
        max_query_tokens: int = 0,
        use_cuda: bool = False,
        device_ids: list[int] | None = None,
    ) -> None:
        self.model_name = model_name
        self.sparse_threads = sparse_threads
        self._max_query_tokens_cfg = max_query_tokens
        self.use_cuda = use_cuda
        self.device_ids = device_ids
        self._truncation_ready = False
        expected = KNOWN_COLBERT_TOKEN_DIMENSIONS.get(model_name)
        self.token_dimension = expected if expected is not None else 128

    @classmethod
    def release_shared(cls) -> None:
        cls._shared_model = None
        cls._shared_tokenizer = None
        trim_memory()

    def is_loaded(self) -> bool:
        return self._shared_model is not None

    def active_device(self) -> str:
        """Return the device ORT actually uses after the model is loaded."""
        if not self.is_loaded():
            return "cpu"
        providers = extract_execution_providers(self._shared_model)
        if any("CUDA" in provider for provider in providers):
            return "cuda"
        return "cpu"

    def execution_providers(self) -> list[str]:
        if not self.is_loaded():
            return []
        return extract_execution_providers(self._shared_model)

    def preload(self) -> None:
        self._get_model()

    def release(self) -> None:
        type(self).release_shared()

    async def embed_batch(self, texts: list[str]) -> list[list[list[float]]]:
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
            role="colbert",
            model_name=self.model_name,
            env_tokens=self._max_query_tokens_cfg,
            model_dir=model_dir,
            known_registry=KNOWN_COLBERT_MODEL_MAX_TOKENS,
        )
        cls._shared_max_tokens = max_tok
        cls._shared_truncation_source = source
        self._truncation_ready = True

    def _get_model(self):
        cls = type(self)
        if cls._shared_model is None:
            from fastembed.late_interaction import LateInteractionTextEmbedding

            threads = resolve_threads(self.sparse_threads)
            _tlog.info(
                "loading_colbert_model model=%s threads=%d cuda=%s device_ids=%s",
                self.model_name,
                threads,
                self.use_cuda,
                self.device_ids,
            )
            t0 = time.monotonic()
            cls._shared_model = LateInteractionTextEmbedding(
                model_name=self.model_name,
                threads=threads,
                cuda=self.use_cuda,
                device_ids=self.device_ids,
            )
            _tlog.info(
                "colbert_model_loaded model=%s elapsed_s=%.2f",
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

    def _to_multivector(self, raw: Any) -> list[list[float]]:
        if hasattr(raw, "tolist"):
            return raw.tolist()
        return [[float(v) for v in row] for row in raw]

    def _embed_batch_sync(self, texts: list[str]) -> list[list[list[float]]]:
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
                    "colbert_chunks_truncated count=%d max_tokens=%d source=%s",
                    truncated_count,
                    self._max_tokens,
                    cls._shared_truncation_source,
                )
            texts = truncated
        _tlog.info("colbert_embed_start chunks=%d", len(texts))
        t0 = time.monotonic()
        result = [self._to_multivector(r) for r in model.embed(texts)]
        _tlog.info(
            "colbert_embed_done chunks=%d elapsed_s=%.2f",
            len(result),
            time.monotonic() - t0,
        )
        return result
