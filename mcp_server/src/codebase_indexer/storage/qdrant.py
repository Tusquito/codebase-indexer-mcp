# src/codebase_indexer/storage/qdrant.py
"""Qdrant vector database wrapper with hybrid (dense + sparse) support."""

import asyncio
import fnmatch
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
    HnswConfigDiff,
    MatchAny,
    MatchValue,
    MultiVectorComparator,
    MultiVectorConfig,
    OptimizersConfigDiff,
    PayloadSchemaType,
    PointStruct,
    Prefetch,
    QuantizationSearchParams,
    RecommendInput,
    RecommendQuery,
    RecommendStrategy,
    ScalarQuantization,
    ScalarQuantizationConfig,
    ScalarType,
    SearchParams,
    SparseIndexParams,
    SparseVectorParams,
    VectorParams,
)
from qdrant_client.models import SparseVector as QdrantSparseVector

from codebase_indexer.config import KNOWN_COLBERT_TOKEN_DIMENSIONS, Settings
from codebase_indexer.indexer.embedder import EmbeddedChunk, SparseVector

log = structlog.get_logger()


@dataclass
class SearchResult:
    """A single code chunk returned from vector search with score and metadata."""

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


def fuse_cross_collection_rrf(
    per_collection_results: list[list[SearchResult]],
    *,
    rrf_k: int,
    top_k: int,
) -> list[SearchResult]:
    """Re-fuse per-collection ranked lists with global RRF.

    Per-collection RRF scores are not comparable across collections; this
    recomputes rank-based RRF from each list and merges globally.
    Original ``SearchResult.score`` values are preserved for display.
    """
    fused_scores: dict[tuple[str, str], float] = {}
    by_key: dict[tuple[str, str], SearchResult] = {}

    for coll_results in per_collection_results:
        for rank, result in enumerate(coll_results, start=1):
            key = (result.collection, result.chunk_id)
            fused_scores[key] = fused_scores.get(key, 0.0) + 1.0 / (rrf_k + rank)
            by_key[key] = result

    # Sort by fused score descending; break ties deterministically on the
    # (collection, chunk_id) key so ranking doesn't depend on collection
    # iteration / dict insertion order.
    ranked_keys = sorted(
        fused_scores,
        key=lambda k: (fused_scores[k], k[0], k[1]),
        reverse=True,
    )
    return [by_key[k] for k in ranked_keys[:top_k]]


@dataclass
class AdaptiveRerankStats:
    """Counters for adaptive ColBERT skip decisions (per storage instance)."""

    total: int = 0
    skipped: int = 0
    reranked: int = 0

    @property
    def skip_rate(self) -> float:
        if self.total == 0:
            return 0.0
        return self.skipped / self.total

    def as_dict(self) -> dict[str, float | int]:
        return {
            "total": self.total,
            "skipped": self.skipped,
            "reranked": self.reranked,
            "skip_rate": round(self.skip_rate, 4),
        }


@dataclass
class CollectionStats:
    """Summary statistics for one indexed Qdrant collection."""

    name: str
    vector_count: int
    disk_size_mb: float
    dense_embed_model: str
    sparse_embed_model: str
    dense_embed_backend: str
    hybrid: bool
    rerank_enabled: bool = False
    colbert_embed_model: str = ""


class QdrantStorage:
    """Async Qdrant client wrapper for hybrid dense+sparse indexing and search.

    Manages collection lifecycle, batched upserts, payload keyword indexes,
    incremental metadata scrolls, and RRF-fused hybrid queries.
    """

    def __init__(self, settings: Settings):
        """Initialize storage with application settings (URL, timeouts, hybrid flags)."""
        self.settings = settings
        self._client: AsyncQdrantClient | None = None
        self._adaptive_stats = AdaptiveRerankStats()

    def reset_adaptive_stats(self) -> None:
        """Reset adaptive rerank skip/rerank counters."""
        self._adaptive_stats = AdaptiveRerankStats()

    @property
    def adaptive_rerank_stats(self) -> AdaptiveRerankStats:
        """Read-only view of adaptive rerank counters."""
        return self._adaptive_stats

    # Payload fields filtered by the query/lookup paths. Keyword indexes here
    # turn full payload scans into indexed lookups (large win as collections grow).
    _INDEXED_PAYLOAD_FIELDS = ("rel_path", "chunk_id", "symbol_name", "language", "callees")

    def _colbert_token_size(self) -> int:
        return KNOWN_COLBERT_TOKEN_DIMENSIONS.get(
            self.settings.colbert_embed_model, 128
        )

    async def _get_client(self) -> AsyncQdrantClient:
        if self._client is None:
            self._client = AsyncQdrantClient(
                url=self.settings.qdrant_url,
                timeout=int(self.settings.qdrant_timeout),
            )
        return self._client

    async def ensure_collection(self, collection: str, retries: int = 5, force: bool = False) -> None:
        """Create hybrid collection if it doesn't exist.

        When ``force=True`` the existing collection is always deleted and
        recreated, giving a clean slate (used by force re-indexing).

        Even when ``force=False``, if the existing collection's dense vector
        dimension differs from ``settings.dense_embed_vector_size``, or its hybrid-search
        configuration (sparse vectors) no longer matches the current settings,
        the collection is automatically deleted and recreated so we never upsert
        vectors of the wrong dimension.
        """
        client = await self._get_client()
        for attempt in range(retries):
            try:
                collections = await client.get_collections()
                existing = {c.name for c in collections.collections}

                if collection in existing:
                    should_recreate = force
                    recreate_reason = "force=True" if force else ""

                    if not should_recreate:
                        # Inspect the collection for dimension / hybrid-config mismatch.
                        info = await client.get_collection(collection)
                        vectors_cfg = info.config.params.vectors
                        existing_dim: int | None = None
                        has_sparse: bool = False
                        has_colbert: bool = False
                        existing_colbert_dim: int | None = None

                        if isinstance(vectors_cfg, dict):
                            dense_cfg = vectors_cfg.get("dense")
                            if dense_cfg is not None:
                                existing_dim = dense_cfg.size
                            has_sparse = "sparse" in vectors_cfg
                            colbert_cfg = vectors_cfg.get("colbert")
                            has_colbert = colbert_cfg is not None
                            if colbert_cfg is not None:
                                existing_colbert_dim = colbert_cfg.size
                        else:
                            # Single (unnamed) vector config — dimension is top-level.
                            if vectors_cfg is not None:
                                existing_dim = vectors_cfg.size

                        if existing_dim is not None and existing_dim != self.settings.dense_embed_vector_size:
                            should_recreate = True
                            recreate_reason = (
                                f"dimension mismatch: collection has {existing_dim}, "
                                f"settings want {self.settings.dense_embed_vector_size}"
                            )
                        elif has_sparse != self.settings.hybrid_search:
                            should_recreate = True
                            recreate_reason = (
                                f"hybrid_search mismatch: collection has sparse={has_sparse}, "
                                f"settings want hybrid_search={self.settings.hybrid_search}"
                            )
                        elif has_colbert != self.settings.rerank_enabled:
                            should_recreate = True
                            recreate_reason = (
                                f"rerank_enabled mismatch: collection has colbert={has_colbert}, "
                                f"settings want rerank_enabled={self.settings.rerank_enabled}"
                            )
                        elif (
                            self.settings.rerank_enabled
                            and existing_colbert_dim is not None
                            and existing_colbert_dim != self._colbert_token_size()
                        ):
                            should_recreate = True
                            recreate_reason = (
                                f"colbert token dimension mismatch: collection has "
                                f"{existing_colbert_dim}, settings want {self._colbert_token_size()}"
                            )

                    if not should_recreate and collection in existing:
                        log.debug(
                            "collection_backend_note",
                            name=collection,
                            dense_embed_backend=self.settings.dense_embed_backend,
                            hint=(
                                "Switching OLLAMA_EMBED_MODEL or dense model requires "
                                "force re-index; existing vectors may be incompatible."
                            ),
                        )

                    if should_recreate:
                        log.warning(
                            "collection_recreating",
                            name=collection,
                            reason=recreate_reason,
                        )
                        await client.delete_collection(collection_name=collection)
                        log.info("collection_deleted_for_recreate", name=collection)
                    else:
                        log.debug("collection_exists", name=collection)
                        if self.settings.payload_indexes:
                            await self._ensure_payload_indexes(client, collection)
                        return

                # At this point the collection either never existed or was just deleted.
                vectors_config = {
                    "dense": VectorParams(
                        size=self.settings.dense_embed_vector_size,
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

                if self.settings.rerank_enabled:
                    vectors_config["colbert"] = VectorParams(
                        size=self._colbert_token_size(),
                        distance=Distance.COSINE,
                        multivector_config=MultiVectorConfig(
                            comparator=MultiVectorComparator.MAX_SIM,
                        ),
                        hnsw_config=HnswConfigDiff(m=0),
                        on_disk=self.settings.vectors_on_disk,
                    )

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
                    hnsw_config=HnswConfigDiff(
                        m=self.settings.hnsw_m,
                        ef_construct=self.settings.hnsw_ef_construct,
                    ),
                    optimizers_config=OptimizersConfigDiff(
                        memmap_threshold=self.settings.memmap_threshold_kb,
                    ),
                )
                log.info(
                    "collection_created",
                    name=collection,
                    hybrid=self.settings.hybrid_search,
                    rerank=self.settings.rerank_enabled,
                    on_disk=self.settings.vectors_on_disk,
                    quantization=self.settings.quantization,
                    dense_embed_vector_size=self.settings.dense_embed_vector_size,
                )

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

    async def delete_collection(self, collection: str) -> bool:
        """Delete a collection if it exists. Returns True if deleted, False if not found."""
        client = await self._get_client()
        try:
            collections = await client.get_collections()
            existing = {c.name for c in collections.collections}
            if collection in existing:
                await client.delete_collection(collection_name=collection)
                log.info("collection_deleted", name=collection)
                return True
            return False
        except Exception as e:
            log.warning("collection_delete_error", name=collection, error=str(e))
            raise

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
        if ec.colbert_vector is not None:
            colbert = ec.colbert_vector
            if hasattr(colbert, "tolist"):
                colbert = colbert.tolist()
            vectors["colbert"] = colbert
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
                "callees": ec.chunk.callees or [],
                "content": ec.chunk.content,
                "file_sha256": ec.chunk.file_sha256,
                "file_mtime": ec.chunk.file_mtime,
            },
        )

    async def upsert_chunks(self, collection: str, embedded_chunks: list[EmbeddedChunk]) -> None:
        """Batch upsert chunks with dense + sparse (+ optional ColBERT) vectors.

        Builds and sends points in sub-batches (size from settings.upsert_batch)
        to stay within Qdrant's HTTP message size limits. With ColBERT rerank
        enabled, each point includes a large multivector payload — keep
        upsert_batch low (typically 10–25) or upserts fail with ReadError.
        See docs/DEPLOYMENT.md#colbert-rerank-qdrant-upsert-batching.

        Points are materialized per sub-batch so at most one sub-batch worth of
        dense vectors is converted to Python lists at a time, keeping peak RAM low.
        """
        client = await self._get_client()
        upsert_batch = self.settings.upsert_batch
        for i in range(0, len(embedded_chunks), upsert_batch):
            batch = [
                self._build_point(ec)
                for ec in embedded_chunks[i : i + upsert_batch]
            ]
            last_exc: Exception | None = None
            for attempt in range(5):
                try:
                    await client.upsert(collection_name=collection, points=batch)
                    break
                except Exception as e:
                    last_exc = e
                    if attempt < 4:
                        err = str(e) or repr(e)
                        log.warning(
                            "upsert_retry",
                            attempt=attempt,
                            batch_offset=i,
                            batch_size=len(batch),
                            error=err,
                        )
                        await asyncio.sleep(min(2**attempt, 8))
                        continue
                    raise last_exc

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

    def _dense_search_params(self) -> SearchParams:
        """Build dense-vector search params (HNSW ef always; quant rescoring when enabled)."""
        quant = None
        if self.settings.quantization:
            quant = QuantizationSearchParams(
                rescore=True,
                oversampling=self.settings.quant_oversampling,
            )
        return SearchParams(quantization=quant, hnsw_ef=self.settings.hnsw_ef)

    async def _hybrid_rrf_query(
        self,
        client: AsyncQdrantClient,
        collection: str,
        dense_vector: list[float],
        sparse_vector: SparseVector,
        *,
        limit: int,
        prefetch_limit: int,
        query_filter: Filter | None,
        dense_params: SearchParams,
    ):
        """Run hybrid dense+sparse prefetch fused with RRF."""
        qdrant_sparse = QdrantSparseVector(
            indices=sparse_vector.indices,
            values=sparse_vector.values,
        )
        return await client.query_points(
            collection_name=collection,
            prefetch=[
                Prefetch(
                    query=dense_vector,
                    using="dense",
                    limit=prefetch_limit,
                    params=dense_params,
                ),
                Prefetch(
                    query=qdrant_sparse,
                    using="sparse",
                    limit=prefetch_limit,
                ),
            ],
            query=FusionQuery(fusion=Fusion.RRF),
            limit=limit,
            query_filter=query_filter,
            with_payload=True,
        )

    def _map_points_to_results(
        self,
        points,
        collection: str,
        *,
        score_threshold: float,
    ) -> list[SearchResult]:
        search_results: list[SearchResult] = []
        for point in points:
            score = point.score if hasattr(point, "score") and point.score is not None else 0.0
            if score < score_threshold:
                continue
            payload = point.payload or {}
            search_results.append(
                SearchResult(
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
                )
            )
        return search_results

    async def _search_single(
        self,
        collection: str,
        dense_vector: list[float],
        sparse_vector: SparseVector | None,
        top_k: int,
        language: str | None,
        min_score: float,
        colbert_vector: list[list[float]] | None = None,
    ) -> list[SearchResult]:
        """Search a single collection."""
        client = await self._get_client()
        query_filter = self._build_query_filter(language)

        used_hybrid = bool(sparse_vector and self.settings.hybrid_search)
        used_rerank = bool(
            self.settings.rerank_enabled
            and colbert_vector is not None
            and used_hybrid
        )
        used_adaptive = bool(
            used_rerank and self.settings.rerank_adaptive_enabled
        )
        dense_params = self._dense_search_params()
        prefetch_limit = (
            self.settings.rerank_prefetch
            if used_rerank
            else top_k * self.settings.prefetch_multiplier
        )
        score_threshold = 0.0 if (used_hybrid or used_rerank) else min_score

        if used_adaptive:
            assert sparse_vector is not None
            probe_limit = max(top_k, 2)
            probe = await self._hybrid_rrf_query(
                client,
                collection,
                dense_vector,
                sparse_vector,
                limit=probe_limit,
                prefetch_limit=prefetch_limit,
                query_filter=query_filter,
                dense_params=dense_params,
            )
            self._adaptive_stats.total += 1
            points = probe.points
            if (
                len(points) >= 2
                and points[0].score - points[1].score >= self.settings.rerank_adaptive_gap
            ):
                gap = points[0].score - points[1].score
                self._adaptive_stats.skipped += 1
                log.debug(
                    "adaptive_rerank_skip",
                    collection=collection,
                    gap=gap,
                    threshold=self.settings.rerank_adaptive_gap,
                )
                return self._map_points_to_results(
                    points[:top_k],
                    collection,
                    score_threshold=score_threshold,
                )
            self._adaptive_stats.reranked += 1

        if used_rerank:
            assert sparse_vector is not None
            assert colbert_vector is not None
            qdrant_sparse = QdrantSparseVector(
                indices=sparse_vector.indices,
                values=sparse_vector.values,
            )
            results = await client.query_points(
                collection_name=collection,
                prefetch=[
                    Prefetch(
                        query=dense_vector,
                        using="dense",
                        limit=prefetch_limit,
                        params=dense_params,
                    ),
                    Prefetch(
                        query=qdrant_sparse,
                        using="sparse",
                        limit=prefetch_limit,
                    ),
                ],
                query=colbert_vector,
                using="colbert",
                limit=top_k,
                query_filter=query_filter,
                with_payload=True,
            )
        elif used_hybrid:
            assert sparse_vector is not None
            results = await self._hybrid_rrf_query(
                client,
                collection,
                dense_vector,
                sparse_vector,
                limit=top_k,
                prefetch_limit=prefetch_limit,
                query_filter=query_filter,
                dense_params=dense_params,
            )
        else:
            results = await client.query_points(
                collection_name=collection,
                query=dense_vector,
                using="dense",
                limit=top_k,
                query_filter=query_filter,
                search_params=dense_params,
                with_payload=True,
            )

        return self._map_points_to_results(
            results.points,
            collection,
            score_threshold=score_threshold,
        )

    async def search(
        self,
        collection: str | None,
        dense_vector: list[float],
        sparse_vector: SparseVector | None,
        top_k: int = 5,
        language: str | None = None,
        min_score: float = 0.5,
        restrict_collections: list[str] | None = None,
        colbert_vector: list[list[float]] | None = None,
    ) -> list[SearchResult]:
        """Search one or multiple collections.

        - collection="name" → search that single collection.
        - collection=None, restrict_collections=["a","b"] → search only those.
        - collection=None, restrict_collections=None → search ALL collections.
        """
        if collection:
            return await self._search_single(
                collection,
                dense_vector,
                sparse_vector,
                top_k,
                language,
                min_score,
                colbert_vector=colbert_vector,
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
                name,
                dense_vector,
                sparse_vector,
                top_k,
                language,
                min_score,
                colbert_vector=colbert_vector,
            )
            for name in coll_names
        ]
        all_results = await asyncio.gather(*tasks, return_exceptions=True)

        per_collection: list[list[SearchResult]] = []
        for r in all_results:
            if isinstance(r, BaseException):
                log.warning("cross_collection_search_error", error=str(r))
                continue
            per_collection.append(r)

        if not per_collection:
            return []

        if len(per_collection) == 1:
            return per_collection[0][:top_k]

        return fuse_cross_collection_rrf(
            per_collection,
            rrf_k=self.settings.rrf_k,
            top_k=top_k,
        )

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

    async def find_chunk_by_id(
        self,
        chunk_id: str,
        collection: str | None = None,
    ) -> dict | None:
        """Retrieve a chunk by ID from one collection or all indexed collections."""
        if collection:
            return await self.get_chunk_by_id(collection, chunk_id)

        stats = await self.list_collection_stats()
        for coll in stats:
            result = await self.get_chunk_by_id(coll.name, chunk_id)
            if result is not None:
                return result
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
                    dense_embed_backend=self.settings.dense_embed_backend,
                    hybrid=self.settings.hybrid_search,
                    rerank_enabled=self.settings.rerank_enabled,
                    colbert_embed_model=(
                        self.settings.colbert_embed_model
                        if self.settings.rerank_enabled
                        else ""
                    ),
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

    async def find_callers_in_collections(
        self,
        method: str,
        collections: list[str],
        receiver: str | None = None,
        limit_per_collection: int = 10,
    ) -> list[SearchResult]:
        """Find all chunks that call a method across multiple collections."""
        client = await self._get_client()
        all_results: list[SearchResult] = []
        token = f"{receiver}.{method}" if receiver else method

        async def _scroll_collection(coll: str) -> list[SearchResult]:
            try:
                points, _ = await client.scroll(
                    collection_name=coll,
                    scroll_filter=Filter(
                        must=[FieldCondition(key="callees", match=MatchValue(value=token))]
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
                log.warning("callers_scroll_error", collection=coll, error=str(e))
                return []

        tasks = [_scroll_collection(c) for c in collections]
        results = await asyncio.gather(*tasks)
        for r in results:
            all_results.extend(r)
        return all_results

    async def scroll_chunks_by_paths(
        self,
        collection: str,
        rel_paths: list[str],
        payload_fields: list[str] | None = None,
        limit: int = 500,
    ) -> list[dict]:
        """Fetch chunk payloads for specific file paths using the keyword index.

        Uses a ``should`` (OR) filter on the indexed ``rel_path`` field so
        Qdrant can satisfy the query without a full-collection scan.

        Args:
            collection: Collection name to query.
            rel_paths: List of rel_path values to retrieve chunks for.
            payload_fields: Payload keys to include (``None`` = all fields).
            limit: Maximum number of points to return.

        Returns:
            List of payload dicts (one per chunk) for the matching paths.
        """
        if not rel_paths:
            return []

        with_payload: bool | list[str] = payload_fields if payload_fields else True

        should_conditions = [
            FieldCondition(key="rel_path", match=MatchAny(any=rel_paths))
        ]

        client = await self._get_client()
        try:
            points, _ = await client.scroll(
                collection_name=collection,
                scroll_filter=Filter(should=should_conditions),
                limit=limit,
                with_payload=with_payload,
                with_vectors=False,
            )
        except Exception as e:
            log.warning(
                "scroll_chunks_by_paths_error",
                collection=collection,
                n_paths=len(rel_paths),
                error=str(e),
            )
            return []

        return [p.payload for p in points if p.payload]

    @staticmethod
    def chunk_id_to_point_id(chunk_id: str) -> str:
        """Map a chunk_id to the deterministic Qdrant point UUID."""
        return str(uuid.uuid5(uuid.NAMESPACE_URL, chunk_id))

    async def verify_chunk_ids_exist(
        self, collection: str, chunk_ids: list[str]
    ) -> None:
        """Raise ValueError listing unknown chunk_ids if any are missing."""
        if not chunk_ids:
            return
        client = await self._get_client()
        point_ids = [self.chunk_id_to_point_id(cid) for cid in chunk_ids]
        records = await client.retrieve(
            collection_name=collection,
            ids=point_ids,
            with_payload=False,
            with_vectors=False,
        )
        found_point_ids = {str(r.id) for r in records}
        unknown = [
            cid
            for cid, pid in zip(chunk_ids, point_ids, strict=True)
            if str(pid) not in found_point_ids
        ]
        if unknown:
            raise ValueError(
                f"Unknown chunk_id(s) in collection {collection!r}: {', '.join(unknown)}"
            )

    async def recommend(
        self,
        collection: str,
        positive: list[str | list[float]],
        negative: list[str | list[float]] | None = None,
        limit: int = 5,
        language: str | None = None,
        path_glob: str | None = None,
    ) -> list[SearchResult]:
        """Recommend chunks similar to positives and dissimilar from negatives.

        ``positive`` / ``negative`` entries are either dense vectors (list[float])
        or point IDs (str UUID from ``chunk_id_to_point_id``).

        Uses Qdrant RecommendQuery with AVERAGE_VECTOR on the dense channel only.
        ``path_glob`` is applied as a post-filter via fnmatch with over-fetch
        ``limit * 3`` when set.
        """
        client = await self._get_client()
        query_filter = self._build_query_filter(language)
        fetch_limit = limit * 3 if path_glob else limit
        dense_params = self._dense_search_params()

        results = await client.query_points(
            collection_name=collection,
            query=RecommendQuery(
                recommend=RecommendInput(
                    positive=positive,
                    negative=negative or [],
                    strategy=RecommendStrategy.AVERAGE_VECTOR,
                )
            ),
            using="dense",
            limit=fetch_limit,
            query_filter=query_filter,
            search_params=dense_params,
            with_payload=True,
        )

        search_results: list[SearchResult] = []
        for point in results.points:
            score = (
                point.score
                if hasattr(point, "score") and point.score is not None
                else 0.0
            )
            payload = point.payload or {}
            search_results.append(
                SearchResult(
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
                )
            )

        if path_glob:
            search_results = [
                r
                for r in search_results
                if fnmatch.fnmatch(r.rel_path, path_glob)
            ][:limit]
        else:
            search_results = search_results[:limit]

        return search_results

