"""One-off live test for adaptive ColBERT rerank skip (run inside MCP container)."""

import asyncio
import time

from codebase_indexer.config import Settings
from codebase_indexer.indexer.backends.factory import create_backends, create_colbert_backend
from codebase_indexer.indexer.embedder import Embedder
from codebase_indexer.storage.qdrant import QdrantStorage

QUERIES = [
    "adaptive rerank skip ColBERT",
    "QdrantStorage search hybrid RRF",
    "docker compose deployment",
    "MCP server health check",
    "embedding model TEI",
    "sparse BM25 hybrid search",
    "index_codebase pipeline",
    "config Settings rerank",
    "ColBERT multivector MAX_SIM",
    "benchmark eval retrieval",
]


async def main() -> None:
    settings = Settings()
    print(
        f"settings: rerank={settings.rerank_enabled} "
        f"adaptive={settings.rerank_adaptive_enabled} "
        f"gap={settings.rerank_adaptive_gap}"
    )
    dense_backend, sparse_backend = create_backends(settings)
    colbert_backend = create_colbert_backend(settings)
    embedder = Embedder(
        dense_backend=dense_backend,
        sparse_backend=sparse_backend,
        dense_embed_vector_size=settings.dense_embed_vector_size,
        hybrid=settings.hybrid_search,
        colbert_backend=colbert_backend,
        rerank=settings.rerank_enabled,
    )
    storage = QdrantStorage(settings)
    storage.reset_adaptive_stats()
    collection = "codebase-indexer-mcp"
    timings_skip: list[float] = []
    timings_rerank: list[float] = []
    for query in QUERIES:
        prev_skipped = storage.adaptive_rerank_stats.skipped
        prev_reranked = storage.adaptive_rerank_stats.reranked
        dense_vec, sparse_vec, colbert_vec = await embedder.embed_query(query)
        t0 = time.perf_counter()
        results = await storage.search(
            collection=collection,
            top_k=5,
            dense_vector=dense_vec,
            sparse_vector=sparse_vec,
            colbert_vector=colbert_vec,
        )
        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        cur = storage.adaptive_rerank_stats
        if cur.skipped > prev_skipped:
            timings_skip.append(elapsed_ms)
        elif cur.reranked > prev_reranked:
            timings_rerank.append(elapsed_ms)
        if results:
            print(f"  q={query[:40]!r} hits={len(results)} top_score={results[0].score:.4f} {elapsed_ms:.1f}ms")
        else:
            print(f"  q={query[:40]!r} hits=0 {elapsed_ms:.1f}ms")
    stats = storage.adaptive_rerank_stats
    print(
        f"adaptive_stats: total={stats.total} skipped={stats.skipped} "
        f"reranked={stats.reranked} skip_rate={stats.skip_rate:.1%}"
    )
    if timings_skip:
        print(f"latency_skip_ms: mean={sum(timings_skip)/len(timings_skip):.1f} n={len(timings_skip)}")
    if timings_rerank:
        print(f"latency_rerank_ms: mean={sum(timings_rerank)/len(timings_rerank):.1f} n={len(timings_rerank)}")


if __name__ == "__main__":
    asyncio.run(main())
