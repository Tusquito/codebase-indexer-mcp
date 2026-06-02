# src/codebase_indexer/storage/qdrant.py
"""Qdrant vector database wrapper with hybrid (dense + sparse) support."""

import asyncio
import uuid
from dataclasses import dataclass

import structlog
from qdrant_client import AsyncQdrantClient
from qdrant_client.models import (
    Distance,
    FieldCondition,
    Filter,
    Fusion,
    FusionQuery,
    MatchValue,
    PointStruct,
    Prefetch,
    SparseIndexParams,
    SparseVectorParams,
    VectorParams,
)

from codebase_indexer.config import Settings
from codebase_indexer.indexer.embedder import EmbeddedChunk, SparseVector

log = structlog.get_logger()


@dataclass
class SearchResult:
    chunk_id: str
    score: float
    rel_path: str
    language: str
    start_line: int
    end_line: int
    symbol_name: str | None
    symbol_type: str
    content: str
    collection: str = ""


@dataclass
class CollectionStats:
    name: str
    vector_count: int
    disk_size_mb: float
    embed_model: str
    hybrid: bool


class QdrantStorage:
    def __init__(self, settings: Settings):
        self.settings = settings
        self._client: AsyncQdrantClient | None = None

    async def _get_client(self) -> AsyncQdrantClient:
        if self._client is None:
            self._client = AsyncQdrantClient(url=self.settings.qdrant_url)
        return self._client

    async def ensure_collection(self, collection: str, retries: int = 5) -> None:
        """Create hybrid collection if it doesn't exist."""
        client = await self._get_client()
        for attempt in range(retries):
            try:
                collections = await client.get_collections()
                existing = {c.name for c in collections.collections}
                if collection not in existing:
                    vectors_config = {
                        "dense": VectorParams(
                            size=self.settings.vector_size,
                            distance=Distance.COSINE,
                        )
                    }
                    sparse_vectors_config = None
                    if self.settings.hybrid_search:
                        sparse_vectors_config = {
                            "sparse": SparseVectorParams(
                                index=SparseIndexParams(on_disk=False)
                            )
                        }
                    await client.create_collection(
                        collection_name=collection,
                        vectors_config=vectors_config,
                        sparse_vectors_config=sparse_vectors_config,
                    )
                    log.info("collection_created", name=collection, hybrid=self.settings.hybrid_search)
                else:
                    log.debug("collection_exists", name=collection)

                return
            except Exception as e:
                if attempt < retries - 1:
                    log.warning("qdrant_retry", attempt=attempt, error=str(e))
                    await asyncio.sleep(2)
                    continue
                raise

    async def upsert_chunks(self, collection: str, embedded_chunks: list[EmbeddedChunk]) -> None:
        """Batch upsert chunks with dense + sparse vectors.

        Splits into sub-batches of 500 points to stay within Qdrant's
        gRPC/HTTP message size limits on large flush batches.
        """
        client = await self._get_client()
        points = []
        for ec in embedded_chunks:
            vectors: dict = {"dense": ec.dense_vector}
            if ec.sparse_vector is not None:
                vectors["sparse"] = {
                    "indices": ec.sparse_vector.indices,
                    "values": ec.sparse_vector.values,
                }

            points.append(
                PointStruct(
                    id=str(uuid.uuid5(uuid.NAMESPACE_URL, ec.chunk.chunk_id)),
                    vector=vectors,
                    payload={
                        "chunk_id": ec.chunk.chunk_id,
                        "rel_path": ec.chunk.rel_path,
                        "language": ec.chunk.language,
                        "start_line": ec.chunk.start_line,
                        "end_line": ec.chunk.end_line,
                        "symbol_name": ec.chunk.symbol_name,
                        "symbol_type": ec.chunk.symbol_type,
                        "content": ec.chunk.content,
                        "file_sha256": ec.chunk.file_sha256,
                        "file_mtime": ec.chunk.file_mtime,
                    },
                )
            )

        # Sub-batch to avoid Qdrant message size limits
        _UPSERT_BATCH = 500
        for i in range(0, len(points), _UPSERT_BATCH):
            batch = points[i : i + _UPSERT_BATCH]
            for attempt in range(3):
                try:
                    await client.upsert(collection_name=collection, points=batch)
                    break
                except Exception as e:
                    if attempt < 2:
                        log.warning("upsert_retry", attempt=attempt, batch_offset=i, error=str(e))
                        await asyncio.sleep(1)
                        continue
                    raise

    async def delete_by_path(self, collection: str, rel_path: str) -> None:
        """Delete all points for a given file path."""
        client = await self._get_client()
        await client.delete(
            collection_name=collection,
            points_selector=Filter(
                must=[FieldCondition(key="rel_path", match=MatchValue(value=rel_path))]
            ),
        )

    async def delete_by_paths(
        self, collection: str, rel_paths: list[str], batch_size: int = 100,
    ) -> None:
        """Delete all points for multiple file paths in batched concurrent calls.

        Uses OR (should) filters to batch deletions, reducing Qdrant round-trips
        from N to ceil(N / batch_size).
        """
        if not rel_paths:
            return
        client = await self._get_client()

        async def _delete_batch(paths: list[str]) -> None:
            await client.delete(
                collection_name=collection,
                points_selector=Filter(
                    should=[
                        FieldCondition(key="rel_path", match=MatchValue(value=p))
                        for p in paths
                    ]
                ),
            )

        tasks = [
            _delete_batch(rel_paths[i:i + batch_size])
            for i in range(0, len(rel_paths), batch_size)
        ]
        await asyncio.gather(*tasks)

    async def get_file_hashes(self, collection: str) -> dict[str, str]:
        """Get {rel_path: file_sha256} for incremental indexing.

        Deprecated: prefer get_file_metadata() which also returns mtime.
        """
        metadata = await self.get_file_metadata(collection)
        return {k: v["sha256"] for k, v in metadata.items()}

    async def get_file_metadata(self, collection: str) -> dict[str, dict]:
        """Get {rel_path: {"sha256": str, "mtime": float | None}} for incremental indexing.

        Returns deduplicated per-file metadata. Uses a larger scroll page size
        to reduce Qdrant round-trips on big collections.
        """
        client = await self._get_client()
        metadata: dict[str, dict] = {}

        try:
            offset = None
            while True:
                result = await client.scroll(
                    collection_name=collection,
                    limit=10000,
                    offset=offset,
                    with_payload=["rel_path", "file_sha256", "file_mtime"],
                    with_vectors=False,
                )
                points, next_offset = result
                for point in points:
                    payload = point.payload or {}
                    rel_path = payload.get("rel_path")
                    file_hash = payload.get("file_sha256")
                    if rel_path and file_hash and rel_path not in metadata:
                        metadata[rel_path] = {
                            "sha256": file_hash,
                            "mtime": payload.get("file_mtime"),
                        }

                if next_offset is None:
                    break
                offset = next_offset
        except Exception as e:
            log.warning("get_metadata_error", error=str(e))

        return metadata

    def _build_query_filter(
        self, language: str | None,
    ) -> Filter | None:
        """Build a Qdrant filter for language."""
        must: list[FieldCondition] = []

        if language:
            must.append(FieldCondition(key="language", match=MatchValue(value=language)))

        if not must:
            return None
        return Filter(must=must)

    async def _search_single(
        self,
        collection: str,
        dense_vector: list[float],
        sparse_vector: SparseVector | None,
        top_k: int,
        language: str | None,
        min_score: float,
    ) -> list[SearchResult]:
        """Search a single collection."""
        client = await self._get_client()
        query_filter = self._build_query_filter(language)

        if sparse_vector and self.settings.hybrid_search:
            sparse_query = {
                "indices": sparse_vector.indices,
                "values": sparse_vector.values,
            }
            results = await client.query_points(
                collection_name=collection,
                prefetch=[
                    Prefetch(query=dense_vector, using="dense", limit=top_k * 3),
                    Prefetch(query=sparse_query, using="sparse", limit=top_k * 3),
                ],
                query=FusionQuery(fusion=Fusion.RRF),
                limit=top_k,
                query_filter=query_filter,
                with_payload=True,
            )
        else:
            results = await client.query_points(
                collection_name=collection,
                query=dense_vector,
                using="dense",
                limit=top_k,
                query_filter=query_filter,
                with_payload=True,
            )

        search_results = []
        for point in results.points:
            score = point.score if hasattr(point, 'score') and point.score is not None else 0.0
            if score < min_score:
                continue
            payload = point.payload or {}
            search_results.append(SearchResult(
                chunk_id=payload.get("chunk_id", ""),
                score=score,
                rel_path=payload.get("rel_path", ""),
                language=payload.get("language", ""),
                start_line=payload.get("start_line", 0),
                end_line=payload.get("end_line", 0),
                symbol_name=payload.get("symbol_name"),
                symbol_type=payload.get("symbol_type", "other"),
                content=payload.get("content", ""),
                collection=collection,
            ))

        return search_results

    async def search(
        self,
        collection: str | None,
        dense_vector: list[float],
        sparse_vector: SparseVector | None,
        top_k: int = 5,
        language: str | None = None,
        min_score: float = 0.5,
        restrict_collections: list[str] | None = None,
    ) -> list[SearchResult]:
        """Search one or multiple collections.

        - collection="name" → search that single collection.
        - collection=None, restrict_collections=["a","b"] → search only those.
        - collection=None, restrict_collections=None → search ALL collections.
        """
        if collection:
            return await self._search_single(
                collection, dense_vector, sparse_vector, top_k, language, min_score,
            )

        # Determine which collections to search
        if restrict_collections:
            coll_names = restrict_collections
        else:
            client = await self._get_client()
            collections = await client.get_collections()
            coll_names = [c.name for c in collections.collections]

        if not coll_names:
            return []

        tasks = [
            self._search_single(
                name, dense_vector, sparse_vector, top_k, language, min_score,
            )
            for name in coll_names
        ]
        all_results = await asyncio.gather(*tasks, return_exceptions=True)

        merged: list[SearchResult] = []
        for r in all_results:
            if isinstance(r, Exception):
                log.warning("cross_collection_search_error", error=str(r))
                continue
            merged.extend(r)

        # Sort by score descending, take top_k
        merged.sort(key=lambda x: x.score, reverse=True)
        return merged[:top_k]

    async def get_chunk_by_id(self, collection: str, chunk_id: str) -> dict | None:
        """Retrieve a specific chunk by its chunk_id."""
        client = await self._get_client()

        result = await client.scroll(
            collection_name=collection,
            scroll_filter=Filter(
                must=[FieldCondition(key="chunk_id", match=MatchValue(value=chunk_id))]
            ),
            limit=1,
            with_payload=True,
            with_vectors=False,
        )
        points, _ = result
        if points:
            return points[0].payload
        return None

    async def list_collection_stats(self) -> list[CollectionStats]:
        """List all collections with stats."""
        client = await self._get_client()
        collections = await client.get_collections()
        stats = []
        for coll in collections.collections:
            try:
                info = await client.get_collection(coll.name)
                # Use optimizer_status to estimate disk size; payload_storage_size
                # was removed in newer qdrant-client versions.
                disk_bytes = getattr(info, "payload_storage_size", None)
                if disk_bytes is None:
                    disk_bytes = getattr(info, "disk_data_size", 0) or 0
                stats.append(CollectionStats(
                    name=coll.name,
                    vector_count=info.points_count or 0,
                    disk_size_mb=round(disk_bytes / 1024 / 1024, 2),
                    embed_model=self.settings.embed_model,
                    hybrid=self.settings.hybrid_search,
                ))
            except Exception as e:
                log.warning("collection_stats_error", name=coll.name, error=str(e))
        return stats

    async def scroll_file_symbols(
        self, collection: str, rel_path: str
    ) -> list[dict]:
        """Return all symbol metadata for a file — no content, no vectors.

        Useful for building a file outline without any embedding cost.
        Results are sorted by start_line ascending.
        """
        client = await self._get_client()
        symbols: list[dict] = []

        try:
            offset = None
            while True:
                points, next_offset = await client.scroll(
                    collection_name=collection,
                    scroll_filter=Filter(
                        must=[FieldCondition(key="rel_path", match=MatchValue(value=rel_path))]
                    ),
                    limit=1000,
                    offset=offset,
                    with_payload=["chunk_id", "symbol_name", "symbol_type", "start_line", "end_line", "language"],
                    with_vectors=False,
                )
                for point in points:
                    payload = point.payload or {}
                    symbols.append({
                        "chunk_id": payload.get("chunk_id", ""),
                        "symbol_name": payload.get("symbol_name"),
                        "symbol_type": payload.get("symbol_type", "other"),
                        "start_line": payload.get("start_line", 0),
                        "end_line": payload.get("end_line", 0),
                        "language": payload.get("language", ""),
                    })
                if next_offset is None:
                    break
                offset = next_offset
        except Exception as e:
            log.warning("scroll_file_symbols_error", collection=collection, rel_path=rel_path, error=str(e))

        symbols.sort(key=lambda s: s["start_line"])
        return symbols

    async def scroll_all_payloads(
        self, collection: str
    ) -> list[dict]:
        """Scroll all points in a collection returning only lightweight payload fields.

        Returns list of dicts with: rel_path, language, symbol_name, symbol_type,
        start_line, end_line. Used to build collection summaries without embedding cost.
        """
        client = await self._get_client()
        rows: list[dict] = []

        try:
            offset = None
            while True:
                points, next_offset = await client.scroll(
                    collection_name=collection,
                    limit=10000,
                    offset=offset,
                    with_payload=["rel_path", "language", "symbol_name", "symbol_type", "start_line", "end_line"],
                    with_vectors=False,
                )
                for point in points:
                    payload = point.payload or {}
                    rows.append({
                        "rel_path": payload.get("rel_path", ""),
                        "language": payload.get("language", ""),
                        "symbol_name": payload.get("symbol_name"),
                        "symbol_type": payload.get("symbol_type", "other"),
                        "start_line": payload.get("start_line", 0),
                        "end_line": payload.get("end_line", 0),
                    })
                if next_offset is None:
                    break
                offset = next_offset
        except Exception as e:
            log.warning("scroll_all_payloads_error", collection=collection, error=str(e))

        return rows

    async def find_symbol_in_collections(
        self,
        symbol_name: str,
        collections: list[str],
        limit_per_collection: int = 10,
    ) -> list[SearchResult]:
        """Find all chunks matching a symbol_name across multiple collections."""
        client = await self._get_client()
        all_results: list[SearchResult] = []

        async def _scroll_collection(coll: str) -> list[SearchResult]:
            try:
                points, _ = await client.scroll(
                    collection_name=coll,
                    scroll_filter=Filter(
                        must=[FieldCondition(key="symbol_name", match=MatchValue(value=symbol_name))]
                    ),
                    limit=limit_per_collection,
                    with_payload=True,
                    with_vectors=False,
                )
                return [
                    SearchResult(
                        chunk_id=p.payload.get("chunk_id", ""),
                        score=0.0,
                        rel_path=p.payload.get("rel_path", ""),
                        language=p.payload.get("language", ""),
                        start_line=p.payload.get("start_line", 0),
                        end_line=p.payload.get("end_line", 0),
                        symbol_name=p.payload.get("symbol_name"),
                        symbol_type=p.payload.get("symbol_type", "other"),
                        content=p.payload.get("content", ""),
                        collection=coll,
                    )
                    for p in points
                    if p.payload
                ]
            except Exception as e:
                log.warning("symbol_scroll_error", collection=coll, error=str(e))
                return []

        tasks = [_scroll_collection(c) for c in collections]
        results = await asyncio.gather(*tasks)
        for r in results:
            all_results.extend(r)
        return all_results

