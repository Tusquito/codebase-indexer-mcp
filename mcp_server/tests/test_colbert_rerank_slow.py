"""Optional slow integration test with real ColBERT model (excluded from default CI)."""

import os
import urllib.request
import uuid

import pytest

from codebase_indexer.config import Settings
from codebase_indexer.indexer.backends.factory import create_colbert_backend
from codebase_indexer.indexer.chunker import Chunk
from codebase_indexer.indexer.embedder import EmbeddedChunk, SparseVector
from codebase_indexer.storage.qdrant import QdrantStorage

QDRANT_URL = os.environ.get("QDRANT_URL", "http://localhost:6333")

pytestmark = [
    pytest.mark.slow,
    pytest.mark.skipif(
        os.environ.get("RUN_SLOW_COLBERT") != "1",
        reason="Set RUN_SLOW_COLBERT=1 to run real ColBERT model test",
    ),
]


def _qdrant_reachable() -> bool:
    try:
        urllib.request.urlopen(f"{QDRANT_URL}/healthz", timeout=2)
        return True
    except Exception:
        try:
            urllib.request.urlopen(f"{QDRANT_URL}/", timeout=2)
            return True
        except Exception:
            return False


@pytest.mark.asyncio
async def test_real_colbert_embed_and_rerank_smoke():
    if not _qdrant_reachable():
        pytest.skip("Qdrant not reachable")

    coll = f"slow_colbert_{uuid.uuid4().hex[:8]}"
    settings = Settings(
        qdrant_url=QDRANT_URL,
        hybrid_search=True,
        rerank_enabled=True,
        rerank_prefetch=10,
        dense_embed_vector_size=768,
    )
    storage = QdrantStorage(settings)
    backend = create_colbert_backend(settings)
    client = await storage._get_client()

    try:
        await storage.ensure_collection(coll)
        texts = [
            "def release_models(): pass",
            "class TestReleaseModels: pass",
        ]
        colbert_vectors = await backend.embed_batch(texts)
        assert len(colbert_vectors) == 2
        assert len(colbert_vectors[0][0]) == 128

        dense = [0.01 * (i + 1) for i in range(768)]
        sparse = SparseVector(indices=[1], values=[1.0])
        for idx, (text, cv) in enumerate(zip(texts, colbert_vectors)):
            chunk = Chunk(
                chunk_id=f"slow.py:{idx}",
                content=text,
                rel_path="slow.py",
                language="python",
                start_line=idx + 1,
                end_line=idx + 2,
                symbol_name="x",
                symbol_type="function",
                file_sha256="h",
                file_mtime=1.0,
            )
            await storage.upsert_chunks(
                coll,
                [
                    EmbeddedChunk(
                        chunk=chunk,
                        dense_vector=dense,
                        sparse_vector=sparse,
                        colbert_vector=cv,
                    )
                ],
            )

        query_cv = (await backend.embed_batch(["release_models implementation"]))[0]
        results = await storage.search(
            collection=coll,
            dense_vector=dense,
            sparse_vector=sparse,
            colbert_vector=query_cv,
            top_k=2,
            min_score=0.0,
        )
        assert len(results) >= 1
    finally:
        backend.release()
        await client.delete_collection(coll)
