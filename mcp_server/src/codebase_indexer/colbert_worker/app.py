"""FastAPI application for ColBERT multivector embedding sidecar."""

from __future__ import annotations

import asyncio
import logging
from contextlib import asynccontextmanager
from typing import Any

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from codebase_indexer.colbert_worker.cuda import cuda_available
from codebase_indexer.colbert_worker.settings import WorkerSettings
from codebase_indexer.indexer.backends.base import EmbeddingError
from codebase_indexer.indexer.backends.colbert_onnx import ColbertOnnxBackend

_tlog = logging.getLogger(__name__)


class ColbertEmbedRequest(BaseModel):
    texts: list[str] = Field(..., min_length=1)


class ColbertEmbedResponse(BaseModel):
    embeddings: list[list[list[float]]]
    token_dimension: int


def _configured_device(settings: WorkerSettings) -> str:
    return "cuda" if settings.colbert_use_cuda else "cpu"


def create_app(
    *,
    settings: WorkerSettings | None = None,
    backend: ColbertOnnxBackend | None = None,
) -> FastAPI:
    settings = settings or WorkerSettings()
    if backend is None:
        backend = ColbertOnnxBackend(
            model_name=settings.colbert_embed_model,
            sparse_threads=settings.sparse_threads,
            max_query_tokens=settings.rerank_max_query_tokens,
            use_cuda=settings.colbert_use_cuda,
            device_ids=settings.colbert_device_ids,
        )

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        if settings.colbert_use_cuda and not cuda_available():
            raise RuntimeError(
                "COLBERT_USE_CUDA=1 but CUDAExecutionProvider is not available"
            )
        loop = asyncio.get_running_loop()
        await loop.run_in_executor(None, backend.preload)
        if settings.colbert_use_cuda and backend.active_device() != "cuda":
            providers = backend.execution_providers()
            raise RuntimeError(
                "COLBERT_USE_CUDA=1 but ColBERT loaded on CPU "
                f"(execution_providers={providers or ['CPUExecutionProvider']})"
            )
        yield

    app = FastAPI(title="colbert_worker", version="0.1.0", lifespan=lifespan)
    app.state.settings = settings
    app.state.backend = backend

    @app.get("/health")
    def health() -> dict[str, Any]:
        device = (
            backend.active_device()
            if backend.is_loaded()
            else _configured_device(settings)
        )
        return {
            "model": settings.colbert_embed_model,
            "token_dimension": backend.token_dimension,
            "loaded": backend.is_loaded(),
            "device": device,
            "execution_providers": backend.execution_providers(),
            "cuda_available": cuda_available(),
        }

    @app.post("/v1/embed/colbert", response_model=ColbertEmbedResponse)
    async def embed_colbert(body: ColbertEmbedRequest) -> ColbertEmbedResponse:
        try:
            embeddings = await backend.embed_batch(body.texts)
        except EmbeddingError as exc:
            _tlog.exception("colbert_worker_embed_failed")
            raise HTTPException(status_code=500, detail=str(exc)) from exc
        except Exception as exc:
            _tlog.exception("colbert_worker_embed_failed")
            raise HTTPException(status_code=500, detail=str(exc)) from exc
        return ColbertEmbedResponse(
            embeddings=embeddings,
            token_dimension=backend.token_dimension,
        )

    return app
