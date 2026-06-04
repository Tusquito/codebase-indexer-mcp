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
    OptimizersConfigDiff,
    PayloadSchemaType,
    PointStruct,
    Prefetch,
    ScalarQuantization,
    ScalarQuantizationConfig,
    ScalarType,
    SparseIndexParams,
    SparseVectorParams,
    VectorParams,
)
from qdrant_client.models import SparseVector as QdrantSparseVector

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
    dense_embed_model: str
    sparse_embed_model: str
    hybrid: bool


class QdrantStorage:
    def __init__(self, settings: Settings):
        self.settings = settings
        self._client: AsyncQdrantClient | None = None

    # Payload fields filtered by the query/lookup paths. Keyword indexes here
    # turn full payload scans into indexed lookups (large win as collections grow).
    _INDEXED_PAYLOAD_FIELDS = ("rel_path", "chunk_id", "symbol_name", "language")

    async def _get_client(self) -> AsyncQdrantClient:
        if self._client is None:
            self._client = AsyncQdrantClient(
                url=self.settings.qdrant_url,
                timeout=int(self.settings.qdrant_timeout),
            )
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
                            # Memory-map dense vectors instead of holding them
                            # fully resident — large RAM saving on big collections.
                            on_disk=self.settings.vectors_on_disk,
                        )
                    }
                    sparse_vectors_config = None
                    if self.settings.hybrid_search:
                        sparse_vectors_config = {
                            "sparse": SparseVectorParams(
                                index=SparseIndexParams(
                                    on_disk=self.settings.sparse_on_disk
                                )
                            )
                        }

                    # int8 scalar quantization: ~4x less vector RAM. Rescoring
                    # against original vectors preserves search quality.
                    quantization_config = None
                    if self.settings.quantization:
                        quantization_config = ScalarQuantization(
                            scalar=ScalarQuantizationConfig(
                                type=ScalarType.INT8,
                                always_ram=True,
                            )
                        )

                    await client.create_collection(
                        collection_name=collection,
                        vectors_config=vectors_config,
                        sparse_vectors_config=sparse_vectors_config,
                        quantization_config=quantization_config,
                        optimizers_config=OptimizersConfigDiff(
                            memmap_threshold=self.settings.memmap_threshold_kb,
                        ),
                    )
                    log.info(
                        "collection_created",
                        name=collection,
                        hybrid=self.settings.hybrid_search,
                        on_disk=self.settings.vectors_on_disk,
                        quantization=self.settings.quantization,
                    )
                else:
                    log.debug("collection_exists", name=collection)

                # Ensure keyword payload indexes exist (best-effort, idempotent).
                # Done on every ensure_collection so pre-existing collections are
                # backfilled on their next (re-)index.
                if self.settings.payload_indexes:
                    await self._ensure_payload_indexes(client, collection)

                return
            except Exception as e:
                if attempt < retries - 1:
                    log.warning("qdrant_retry", attempt=attempt, error=str(e))
                    await asyncio.sleep(2)
                    continue
                raise

    async def _ensure_payload_indexes(
        self, client: AsyncQdrantClient, collection: str
    ) -> None:
        """Create keyword payload indexes for the filtered fields (idempotent).

        Creating an index that already exists is a no-op on the Qdrant side, so
        this is safe to call on every ensure_collection. Failures are logged and
        swallowed — search still works without the index, just slower.
        """
        for field in self._INDEXED_PAYLOAD_FIELDS:
            try:
                await client.create_payload_index(
                    collection_name=collection,
                    field_name=field,
                    field_schema=PayloadSchemaType.KEYWORD,
                )
            except Exception as e:
                log.debug("payload_index_skip", collection=collection, field=field, error=str(e))

    async def set_indexing(self, collection: str, enabled: bool) -> None:
        """Pause or resume HNSW index building for a collection.

        Setting indexing_threshold=0 disables HNSW construction so a bulk
        upload doesn't compete with embedding for CPU. Restore to a positive
        threshold afterwards to let Qdrant build the index in one pass.
        """
        client = await self._get_client()
        threshold = 20000 if enabled else 0
        try:
            await client.update_collection(
                collection_name=collection,
                optimizers_config=OptimizersConfigDiff(indexing_threshold=threshold),
            )
            log.info("indexing_threshold_set", collection=collection, enabled=enabled, threshold=threshold)
        except Exception as e:
            log.warning("set_indexing_error", collection=collection, enabled=enabled, error=str(e))

    def _build_point(self, ec: EmbeddedChunk) -> PointStruct:
        """Build a Qdrant point, converting the numpy dense vector to a list."""
        dense = ec.dense_vector
        # Lazily convert numpy float32 array -> plain list only at send time,
        # so the held/double-buffered batch keeps the compact numpy form.
        if hasattr(dense, "tolist"):
            dense = dense.tolist()
        vectors: dict = {"dense": dense}
        if ec.sparse_vector is not None:
            vectors["sparse"] = {
                "indices": ec.sparse_vector.indices,
                "values": ec.sparse_vector.values,
            }
        return PointStruct(
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

    async def upsert_chunks(self, collection: str, embedded_chunks: list[EmbeddedChunk]) -> None:
        """Batch upsert chunks with dense + sparse vectors.

        Builds and sends points in sub-batches (size from settings.upsert_batch)
        to stay within Qdrant's gRPC/HTTP message size limits. Points are
        materialized per sub-batch so at most one sub-batch worth of dense
        vectors is converted to Python lists at a time, keeping peak RAM low.
        """
        client = await self._get_client()
        upsert_batch = self.settings.upsert_batch
        for i in range(0, len(embedded_chunks), upsert_batch):
            batch = [
                self._build_point(ec)
                for ec in embedded_chunks[i : i + upsert_batch]
            ]
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
        return Filter(must=must)  # type: ignore[arg-type]

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

        used_hybrid = bool(sparse_vector and self.settings.hybrid_search)

        if used_hybrid:
            assert sparse_vector is not None
            qdrant_sparse = QdrantSparseVector(
                indices=sparse_vector.indices,
                values=sparse_vector.values,
            )
            results = await client.query_points(
                collection_name=collection,
                prefetch=[
                    Prefetch(query=dense_vector, using="dense", limit=top_k * 3),
                    Prefetch(query=qdrant_sparse, using="sparse", limit=top_k * 3),
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

        # RRF fusion scores are NOT on the cosine [0,1] similarity scale, so a
        # cosine-calibrated min_score (e.g. 0.5) silently drops most relevant
        # hybrid hits. Apply the cosine threshold only on the pure-dense path;
        # hybrid relies on RRF ranking + top_k instead.
        score_threshold = 0.0 if used_hybrid else min_score

        search_results = []
        for point in results.points:
            score = point.score if hasattr(point, 'score') and point.score is not None else 0.0
            if score < score_threshold:
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
            if isinstance(r, BaseException):
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
                    dense_embed_model=self.settings.dense_embed_model,
                    sparse_embed_model=self.settings.sparse_embed_model,
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

