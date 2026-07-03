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


def _dense_dim() -> int:
    return Settings().dense_embed_vector_size


def _make_embedded(
    rel_path: str,
    start_line: int,
    text: str,
    *,
    dense_scale: float = 1.0,
    dense_dim: int | None = None,
    colbert: list[list[float]] | None = None,
) -> EmbeddedChunk:
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
    dim = dense_dim if dense_dim is not None else _dense_dim()
    dense = [0.01 * dense_scale * ((start_line + i) % 7 + 1) for i in range(dim)]
    sparse = SparseVector(indices=[1, 5, 9], values=[0.5, 0.3, 0.2])
    return EmbeddedChunk(
        chunk=chunk,
        dense_vector=dense,
        sparse_vector=sparse,
        colbert_vector=colbert,
    )


@pytest.fixture
def storage():
    return QdrantStorage(Settings(qdrant_url=QDRANT_URL, hybrid_search=True))


@pytest.mark.asyncio
async def test_ensure_collection_recreates_on_dimension_mismatch():
    """ensure_collection auto-recreates when the dense vector dimension changes."""
    coll = f"test_dim_{uuid.uuid4().hex[:8]}"
    # Create with dim=384 (model must match KNOWN_EMBED_MODEL_DIMENSIONS).
    s384 = Settings(
        qdrant_url=QDRANT_URL,
        hybrid_search=False,
        dense_embed_model="BAAI/bge-small-en-v1.5",
        dense_embed_vector_size=384,
    )
    st384 = QdrantStorage(s384)
    client = await st384._get_client()
    try:
        await st384.ensure_collection(coll)
        info = await client.get_collection(coll)
        assert info.config.params.vectors["dense"].size == 384

        # Now call ensure_collection with dim=768 — should auto-recreate.
        s768 = Settings(
            qdrant_url=QDRANT_URL,
            hybrid_search=False,
            dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
            dense_embed_vector_size=768,
        )
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
    dim = _dense_dim()
    s = Settings(qdrant_url=QDRANT_URL, hybrid_search=True, dense_embed_vector_size=dim)
    st = QdrantStorage(s)
    client = await st._get_client()
    try:
        await st.ensure_collection(coll)
        # Upsert one chunk so we can verify the collection was wiped.
        embedded = [_make_embedded("x.py", 1, "hello", dense_dim=dim)]
        await st.upsert_chunks(coll, embedded)
        meta_before = await st.get_file_metadata(coll)
        assert "x.py" in meta_before

        # force=True must produce an empty collection.
        await st.ensure_collection(coll, force=True)
        meta_after = await st.get_file_metadata(coll)
        assert meta_after == {}

        # Verify the collection still has the right dimension.
        info = await client.get_collection(coll)
        assert info.config.params.vectors["dense"].size == dim
    finally:
        await client.delete_collection(coll)


@pytest.mark.asyncio
async def test_ensure_collection_recreates_on_hybrid_mismatch():
    """ensure_collection auto-recreates when hybrid_search flag changes."""
    coll = f"test_hybrid_{uuid.uuid4().hex[:8]}"
    dim = _dense_dim()
    # Create without sparse vectors.
    s_dense = Settings(qdrant_url=QDRANT_URL, hybrid_search=False, dense_embed_vector_size=dim)
    st_dense = QdrantStorage(s_dense)
    client = await st_dense._get_client()
    try:
        await st_dense.ensure_collection(coll)
        info = await client.get_collection(coll)
        assert "sparse" not in (info.config.params.vectors or {})

        # Now request hybrid — should recreate with sparse vectors.
        s_hybrid = Settings(qdrant_url=QDRANT_URL, hybrid_search=True, dense_embed_vector_size=dim)
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


def _unit_vec(dim: int, index: int, scale: float = 1.0) -> list[float]:
    vec = [0.0] * dim
    vec[index] = scale
    return vec


@pytest.mark.asyncio
async def test_colbert_rerank_reorders_hybrid_candidates():
    """Synthetic multivectors: ColBERT rerank should promote the better MAX_SIM match."""
    coll = f"test_rerank_{uuid.uuid4().hex[:8]}"
    colbert_dim = 128
    dense_dim = _dense_dim()
    s = Settings(
        qdrant_url=QDRANT_URL,
        hybrid_search=True,
        rerank_enabled=True,
        rerank_prefetch=20,
        dense_embed_vector_size=dense_dim,
    )
    st = QdrantStorage(s)
    client = await st._get_client()
    try:
        await st.ensure_collection(coll)

        # Identical dense/sparse so hybrid RRF cannot distinguish them.
        shared_dense = [0.01] * dense_dim
        shared_sparse = SparseVector(indices=[1, 2], values=[0.4, 0.6])
        colbert_a = [_unit_vec(colbert_dim, 0), _unit_vec(colbert_dim, 1)]
        colbert_b = [_unit_vec(colbert_dim, 2)]

        embedded = [
            _make_embedded("a.py", 1, "alpha chunk", dense_dim=dense_dim, colbert=colbert_a),
            _make_embedded("b.py", 1, "beta chunk", dense_dim=dense_dim, colbert=colbert_b),
        ]
        embedded[0] = EmbeddedChunk(
            chunk=embedded[0].chunk,
            dense_vector=shared_dense,
            sparse_vector=shared_sparse,
            colbert_vector=colbert_a,
        )
        embedded[1] = EmbeddedChunk(
            chunk=embedded[1].chunk,
            dense_vector=shared_dense,
            sparse_vector=shared_sparse,
            colbert_vector=colbert_b,
        )
        await st.upsert_chunks(coll, embedded)

        query_colbert = [_unit_vec(colbert_dim, 2)]
        reranked = await st.search(
            collection=coll,
            dense_vector=shared_dense,
            sparse_vector=shared_sparse,
            colbert_vector=query_colbert,
            top_k=2,
            min_score=0.5,
        )
        assert len(reranked) >= 2
        assert reranked[0].rel_path == "b.py"

        hybrid_only = await st.search(
            collection=coll,
            dense_vector=shared_dense,
            sparse_vector=shared_sparse,
            colbert_vector=None,
            top_k=2,
            min_score=0.5,
        )
        assert len(hybrid_only) >= 2
        assert {r.rel_path for r in hybrid_only} == {"a.py", "b.py"}
    finally:
        await client.delete_collection(coll)


@pytest.mark.asyncio
async def test_recommend_excludes_negative_paths_with_path_glob(storage):
    """Recommendation with negative example and path_glob excludes test paths."""
    coll = f"test_recommend_{uuid.uuid4().hex[:8]}"
    client = await storage._get_client()
    try:
        await storage.ensure_collection(coll)

        dense_dim = _dense_dim()
        main_dense = [1.0 if i < 10 else 0.01 * (i % 7) for i in range(dense_dim)]
        util_dense = [0.95 if i < 10 else 0.01 * ((i + 1) % 7) for i in range(dense_dim)]
        test_dense = [0.01 * (i % 7) for i in range(dense_dim)]

        main_chunk = _make_embedded("src/main.py", 1, "def main(): pass", dense_scale=0.0, dense_dim=dense_dim)
        util_chunk = _make_embedded("src/util.py", 1, "def util(): pass", dense_scale=0.0, dense_dim=dense_dim)
        test_chunk = _make_embedded("tests/test_main.py", 1, "def test_main(): pass", dense_scale=0.0, dense_dim=dense_dim)

        embedded = [
            EmbeddedChunk(chunk=main_chunk.chunk, dense_vector=main_dense, sparse_vector=main_chunk.sparse_vector),
            EmbeddedChunk(chunk=util_chunk.chunk, dense_vector=util_dense, sparse_vector=util_chunk.sparse_vector),
            EmbeddedChunk(chunk=test_chunk.chunk, dense_vector=test_dense, sparse_vector=test_chunk.sparse_vector),
        ]
        await storage.upsert_chunks(coll, embedded)

        pos_id = storage.chunk_id_to_point_id("src/main.py:1")
        neg_id = storage.chunk_id_to_point_id("tests/test_main.py:1")

        results = await storage.recommend(
            collection=coll,
            positive=[pos_id],
            negative=[neg_id],
            limit=5,
            path_glob="src/*.py",
        )

        rel_paths = {r.rel_path for r in results}
        assert "tests/test_main.py" not in rel_paths
        assert rel_paths.issubset({"src/main.py", "src/util.py"})
        assert len(results) >= 1
    finally:
        await client.delete_collection(coll)


@pytest.mark.asyncio
async def test_find_outlier_chunks_returns_orthogonal_vector(storage):
    """Outlier with low cosine similarity to cluster centroid is returned."""
    coll = f"test_outlier_{uuid.uuid4().hex[:8]}"
    client = await storage._get_client()
    try:
        await storage.ensure_collection(coll)

        dense_dim = _dense_dim()
        cluster_dense = [1.0 if i < 20 else 0.01 * (i % 5) for i in range(dense_dim)]
        outlier_dense = [0.01 * (i % 5) if i < 20 else 1.0 for i in range(dense_dim)]

        cluster_chunks = [
            _make_embedded(f"src/module_{n}.py", 1, f"def fn{n}(): pass", dense_scale=0.0, dense_dim=dense_dim)
            for n in range(3)
        ]
        for ec in cluster_chunks:
            ec.dense_vector = cluster_dense

        outlier_chunk = _make_embedded(
            "src/orphan.py", 1, "def orphan(): pass", dense_scale=0.0, dense_dim=dense_dim
        )
        outlier_chunk.dense_vector = outlier_dense

        await storage.upsert_chunks(coll, cluster_chunks + [outlier_chunk])

        context_ids = [ec.chunk.chunk_id for ec in cluster_chunks]

        results = await storage.find_outlier_chunks(
            collection=coll,
            context_chunk_ids=context_ids,
            limit=5,
            max_similarity=0.55,
        )

        assert len(results) >= 1
        outlier_hits = [r for r in results if r.rel_path == "src/orphan.py"]
        assert len(outlier_hits) == 1
        assert outlier_hits[0].score < 0.55
        assert outlier_hits[0].chunk_id == "src/orphan.py:1"
    finally:
        await client.delete_collection(coll)


@pytest.mark.asyncio
async def test_verify_chunk_ids_exist_integration(storage):
    """verify_chunk_ids_exist fails fast on missing chunk_id against live Qdrant."""
    coll = f"test_verify_{uuid.uuid4().hex[:8]}"
    client = await storage._get_client()
    try:
        await storage.ensure_collection(coll)
        embedded = [_make_embedded("only.py", 1, "x = 1")]
        await storage.upsert_chunks(coll, embedded)

        await storage.verify_chunk_ids_exist(coll, ["only.py:1"])

        with pytest.raises(ValueError, match="ghost.py:99"):
            await storage.verify_chunk_ids_exist(coll, ["only.py:1", "ghost.py:99"])
    finally:
        await client.delete_collection(coll)
