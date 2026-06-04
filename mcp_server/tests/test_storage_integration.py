"""Integration tests for QdrantStorage against an ephemeral Qdrant.

Skipped automatically when no Qdrant is reachable at QDRANT_URL (default
http://localhost:6333), so the unit suite still runs offline. CI starts a
Qdrant service container so these execute there.
"""

import os
import urllib.request
import uuid

import pytest

from codebase_indexer.config import Settings
from codebase_indexer.indexer.chunker import Chunk
from codebase_indexer.indexer.embedder import EmbeddedChunk, SparseVector
from codebase_indexer.storage.qdrant import QdrantStorage

QDRANT_URL = os.environ.get("QDRANT_URL", "http://localhost:6333")


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


pytestmark = pytest.mark.skipif(
    not _qdrant_reachable(), reason="Qdrant not reachable at QDRANT_URL"
)


def _make_embedded(rel_path: str, start_line: int, text: str) -> EmbeddedChunk:
    chunk = Chunk(
        chunk_id=f"{rel_path}:{start_line}",
        content=text,
        rel_path=rel_path,
        language="python",
        start_line=start_line,
        end_line=start_line + 1,
        symbol_name="thing",
        symbol_type="function",
        file_sha256="hash123",
        file_mtime=1.0,
    )
    # Deterministic non-zero dense vector (768 dims) + tiny sparse vector.
    dense = [0.01 * ((start_line + i) % 7 + 1) for i in range(768)]
    sparse = SparseVector(indices=[1, 5, 9], values=[0.5, 0.3, 0.2])
    return EmbeddedChunk(chunk=chunk, dense_vector=dense, sparse_vector=sparse)


@pytest.fixture
def storage():
    return QdrantStorage(Settings(qdrant_url=QDRANT_URL, hybrid_search=True))


@pytest.mark.asyncio
async def test_ensure_collection_recreates_on_dimension_mismatch():
    """ensure_collection auto-recreates when the dense vector dimension changes."""
    coll = f"test_dim_{uuid.uuid4().hex[:8]}"
    # Create with dim=384
    s384 = Settings(qdrant_url=QDRANT_URL, hybrid_search=False, dense_embed_vector_size=384)
    st384 = QdrantStorage(s384)
    client = await st384._get_client()
    try:
        await st384.ensure_collection(coll)
        info = await client.get_collection(coll)
        assert info.config.params.vectors["dense"].size == 384

        # Now call ensure_collection with dim=768 — should auto-recreate.
        s768 = Settings(qdrant_url=QDRANT_URL, hybrid_search=False, dense_embed_vector_size=768)
        st768 = QdrantStorage(s768)
        st768._client = client  # share the same underlying connection
        await st768.ensure_collection(coll)

        info2 = await client.get_collection(coll)
        assert info2.config.params.vectors["dense"].size == 768
    finally:
        await client.delete_collection(coll)


@pytest.mark.asyncio
async def test_ensure_collection_force_recreates_matching_dimension():
    """ensure_collection with force=True always recreates even when dims match."""
    coll = f"test_force_{uuid.uuid4().hex[:8]}"
    s = Settings(qdrant_url=QDRANT_URL, hybrid_search=True, dense_embed_vector_size=768)
    st = QdrantStorage(s)
    client = await st._get_client()
    try:
        await st.ensure_collection(coll)
        # Upsert one chunk so we can verify the collection was wiped.
        embedded = [_make_embedded("x.py", 1, "hello")]
        await st.upsert_chunks(coll, embedded)
        meta_before = await st.get_file_metadata(coll)
        assert "x.py" in meta_before

        # force=True must produce an empty collection.
        await st.ensure_collection(coll, force=True)
        meta_after = await st.get_file_metadata(coll)
        assert meta_after == {}

        # Verify the collection still has the right dimension.
        info = await client.get_collection(coll)
        assert info.config.params.vectors["dense"].size == 768
    finally:
        await client.delete_collection(coll)


@pytest.mark.asyncio
async def test_ensure_collection_recreates_on_hybrid_mismatch():
    """ensure_collection auto-recreates when hybrid_search flag changes."""
    coll = f"test_hybrid_{uuid.uuid4().hex[:8]}"
    # Create without sparse vectors.
    s_dense = Settings(qdrant_url=QDRANT_URL, hybrid_search=False, dense_embed_vector_size=768)
    st_dense = QdrantStorage(s_dense)
    client = await st_dense._get_client()
    try:
        await st_dense.ensure_collection(coll)
        info = await client.get_collection(coll)
        assert "sparse" not in (info.config.params.vectors or {})

        # Now request hybrid — should recreate with sparse vectors.
        s_hybrid = Settings(qdrant_url=QDRANT_URL, hybrid_search=True, dense_embed_vector_size=768)
        st_hybrid = QdrantStorage(s_hybrid)
        st_hybrid._client = client
        await st_hybrid.ensure_collection(coll)

        info2 = await client.get_collection(coll)
        assert info2.config.params.sparse_vectors is not None
    finally:
        await client.delete_collection(coll)


@pytest.mark.asyncio
async def test_upsert_search_and_metadata_roundtrip(storage):
    coll = f"test_ci_{uuid.uuid4().hex[:8]}"
    client = await storage._get_client()
    try:
        await storage.ensure_collection(coll)

        embedded = [
            _make_embedded("a.py", 1, "def thing(): return 1"),
            _make_embedded("a.py", 10, "def other(): return 2"),
            _make_embedded("b.py", 1, "class C: pass"),
        ]
        await storage.upsert_chunks(coll, embedded)

        # Metadata reflects two distinct files.
        meta = await storage.get_file_metadata(coll)
        assert set(meta.keys()) == {"a.py", "b.py"}
        assert meta["a.py"]["sha256"] == "hash123"

        # Hybrid search returns hits (min_score must NOT filter RRF results).
        results = await storage.search(
            collection=coll,
            dense_vector=embedded[0].dense_vector,
            sparse_vector=embedded[0].sparse_vector,
            top_k=5,
            min_score=0.5,
        )
        assert len(results) >= 1

        # File-scoped deletion removes only the targeted path.
        await storage.delete_by_paths(coll, ["a.py"])
        meta_after = await storage.get_file_metadata(coll)
        assert set(meta_after.keys()) == {"b.py"}
    finally:
        await client.delete_collection(coll)
