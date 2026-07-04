"""Neo4j graph storage wrapper for optional GraphRAG (ADR 0002)."""

from __future__ import annotations

import asyncio

import structlog
from neo4j import AsyncGraphDatabase, AsyncDriver

from codebase_indexer.config import Settings
from codebase_indexer.indexer.graph_writer import GraphBatch
from codebase_indexer.storage.qdrant import SearchResult

log = structlog.get_logger()

_SCHEMA_STATEMENTS = (
    "CREATE CONSTRAINT chunk_id_unique IF NOT EXISTS "
    "FOR (c:Chunk) REQUIRE c.chunk_id IS UNIQUE",
    "CREATE CONSTRAINT file_collection_path_unique IF NOT EXISTS "
    "FOR (f:File) REQUIRE (f.collection, f.rel_path) IS UNIQUE",
    "CREATE CONSTRAINT symbol_qualified_name_unique IF NOT EXISTS "
    "FOR (s:Symbol) REQUIRE s.qualified_name IS UNIQUE",
    "CREATE CONSTRAINT collection_name_unique IF NOT EXISTS "
    "FOR (col:Collection) REQUIRE col.name IS UNIQUE",
    "CREATE CONSTRAINT artifact_key_unique IF NOT EXISTS "
    "FOR (a:Artifact) REQUIRE a.key IS UNIQUE",
    "CREATE INDEX endpoint_collection_path IF NOT EXISTS "
    "FOR (e:Endpoint) ON (e.collection, e.path)",
    "CREATE INDEX symbol_name_collection IF NOT EXISTS "
    "FOR (s:Symbol) ON (s.collection, s.name)",
    "CREATE INDEX calls_call_token IF NOT EXISTS "
    "FOR ()-[r:CALLS]-() ON (r.call_token)",
)


class Neo4jStorage:
    """Async-friendly Neo4j client for index-time graph ingestion."""

    def __init__(self, settings: Settings, driver: AsyncDriver | None = None) -> None:
        self._settings = settings
        self._driver = driver
        self._schema_ready = False

    @property
    def enabled(self) -> bool:
        return self._settings.graph_enabled

    async def _get_driver(self) -> AsyncDriver:
        if self._driver is None:
            self._driver = AsyncGraphDatabase.driver(
                self._settings.neo4j_uri,
                auth=(self._settings.neo4j_user, self._settings.neo4j_password),
            )
        return self._driver

    async def close(self) -> None:
        if self._driver is not None:
            await self._driver.close()
            self._driver = None
            self._schema_ready = False

    async def ensure_schema(self) -> None:
        """Create idempotent constraints and indexes."""
        if not self.enabled or self._schema_ready:
            return

        driver = await self._get_driver()
        async with driver.session(database=self._settings.neo4j_database) as session:
            for statement in _SCHEMA_STATEMENTS:
                await session.run(statement)
            await session.run(
                "MERGE (m:GraphMeta {id: 'schema'}) "
                "SET m.version = $version",
                version=self._settings.graph_schema_version,
            )
        self._schema_ready = True
        log.info(
            "neo4j_schema_ready",
            database=self._settings.neo4j_database,
            schema_version=self._settings.graph_schema_version,
        )

    async def delete_files(self, collection: str, rel_paths: list[str]) -> None:
        """Remove File/Chunk subgraphs for the given paths."""
        if not self.enabled or not rel_paths:
            return

        driver = await self._get_driver()
        async with driver.session(database=self._settings.neo4j_database) as session:
            await session.run(
                """
                UNWIND $paths AS rel_path
                MATCH (f:File {collection: $collection, rel_path: rel_path})
                OPTIONAL MATCH (f)<-[:IN_FILE]-(ch:Chunk)
                DETACH DELETE ch, f
                """,
                collection=collection,
                paths=rel_paths,
            )
        log.debug("neo4j_deleted_files", collection=collection, count=len(rel_paths))

    async def find_callers(
        self,
        method: str,
        collections: list[str],
        receiver: str | None = None,
        limit_per_collection: int = 10,
    ) -> list[SearchResult]:
        """Find caller chunks via CALLS.call_token (ADR 0023 Phase 1)."""
        if not self.enabled or not collections:
            return []

        token = f"{receiver}.{method}" if receiver else method
        driver = await self._get_driver()

        async def _query_collection(coll: str) -> list[SearchResult]:
            async with driver.session(database=self._settings.neo4j_database) as session:
                result = await session.run(
                    """
                    MATCH (col:Collection {name: $collection})<-[:IN_COLLECTION]-(f:File)
                          <-[:IN_FILE]-(ch:Chunk)-[r:CALLS]->(s:Symbol)
                    WHERE r.call_token IN $tokens
                    OPTIONAL MATCH (ch)-[:DEFINES]->(def:Symbol)
                    RETURN ch.chunk_id AS chunk_id,
                           f.rel_path AS rel_path,
                           ch.start_line AS start_line,
                           ch.end_line AS end_line,
                           f.language AS language,
                           def.name AS symbol_name,
                           coalesce(def.kind, 'other') AS symbol_type
                    LIMIT $limit
                    """,
                    collection=coll,
                    tokens=[token],
                    limit=limit_per_collection,
                )
                records = await result.data()
                return [
                    SearchResult(
                        chunk_id=rec["chunk_id"],
                        score=0.0,
                        rel_path=rec["rel_path"],
                        language=rec.get("language") or "",
                        start_line=rec["start_line"] or 0,
                        end_line=rec["end_line"] or 0,
                        symbol_name=rec.get("symbol_name"),
                        symbol_type=rec.get("symbol_type") or "other",
                        content="",
                        collection=coll,
                    )
                    for rec in records
                ]

        results = await asyncio.gather(*[_query_collection(c) for c in collections])
        all_results: list[SearchResult] = []
        for batch in results:
            all_results.extend(batch)
        return all_results

    async def write_batch(self, batch: GraphBatch) -> None:
        """Upsert one graph batch produced by the index-time graph writer."""
        if not self.enabled:
            return

        driver = await self._get_driver()
        async with driver.session(database=self._settings.neo4j_database) as session:
            await self._write_batch_session(session, batch)

    async def _write_batch_session(self, session, batch: GraphBatch) -> None:
        collection = batch.collection

        if batch.collection_props:
            await session.run(
                """
                MERGE (col:Collection {name: $name})
                SET col.schema_version = $schema_version
                """,
                name=collection,
                schema_version=batch.schema_version,
            )

        for slice_start in range(0, len(batch.files), self._settings.graph_writer_batch):
            files = batch.files[slice_start : slice_start + self._settings.graph_writer_batch]
            await session.run(
                """
                UNWIND $files AS row
                MERGE (col:Collection {name: $collection})
                MERGE (f:File {collection: $collection, rel_path: row.rel_path})
                SET f.language = row.language,
                    f.sha256 = row.sha256
                MERGE (f)-[:IN_COLLECTION]->(col)
                """,
                collection=collection,
                files=files,
            )

        for slice_start in range(0, len(batch.chunks), self._settings.graph_writer_batch):
            chunks = batch.chunks[slice_start : slice_start + self._settings.graph_writer_batch]
            await session.run(
                """
                UNWIND $chunks AS row
                MATCH (f:File {collection: $collection, rel_path: row.rel_path})
                MERGE (ch:Chunk {chunk_id: row.chunk_id})
                SET ch.start_line = row.start_line,
                    ch.end_line = row.end_line,
                    ch.collection = $collection
                MERGE (ch)-[:IN_FILE]->(f)
                """,
                collection=collection,
                chunks=chunks,
            )

        for slice_start in range(0, len(batch.defines), self._settings.graph_writer_batch):
            rows = batch.defines[slice_start : slice_start + self._settings.graph_writer_batch]
            await session.run(
                """
                UNWIND $rows AS row
                MATCH (ch:Chunk {chunk_id: row.chunk_id})
                MERGE (s:Symbol {qualified_name: row.qualified_name})
                SET s.name = row.name,
                    s.kind = row.kind,
                    s.collection = $collection
                MERGE (ch)-[:DEFINES]->(s)
                """,
                collection=collection,
                rows=rows,
            )

        for slice_start in range(0, len(batch.calls), self._settings.graph_writer_batch):
            rows = batch.calls[slice_start : slice_start + self._settings.graph_writer_batch]
            await session.run(
                """
                UNWIND $rows AS row
                MATCH (ch:Chunk {chunk_id: row.chunk_id})
                MERGE (s:Symbol {qualified_name: row.qualified_name})
                ON CREATE SET s.name = row.name,
                              s.kind = 'callee',
                              s.collection = $collection
                SET s.collection = $collection
                MERGE (ch)-[r:CALLS]->(s)
                SET r.call_token = row.call_token
                """,
                collection=collection,
                rows=rows,
            )

        for slice_start in range(0, len(batch.imports), self._settings.graph_writer_batch):
            rows = batch.imports[slice_start : slice_start + self._settings.graph_writer_batch]
            await session.run(
                """
                UNWIND $rows AS row
                MATCH (f:File {collection: $collection, rel_path: row.rel_path})
                MERGE (s:Symbol {qualified_name: row.qualified_name})
                SET s.name = row.name,
                    s.kind = 'import',
                    s.collection = $collection
                MERGE (f)-[:IMPORTS]->(s)
                """,
                collection=collection,
                rows=rows,
            )

        for slice_start in range(0, len(batch.endpoints), self._settings.graph_writer_batch):
            rows = batch.endpoints[slice_start : slice_start + self._settings.graph_writer_batch]
            await session.run(
                """
                UNWIND $rows AS row
                MERGE (e:Endpoint {collection: $collection, path: row.path})
                SET e.method = coalesce(row.method, e.method, '')
                """,
                collection=collection,
                rows=rows,
            )

        for slice_start in range(0, len(batch.declares_endpoint), self._settings.graph_writer_batch):
            rows = batch.declares_endpoint[
                slice_start : slice_start + self._settings.graph_writer_batch
            ]
            await session.run(
                """
                UNWIND $rows AS row
                MATCH (ch:Chunk {chunk_id: row.chunk_id})
                MERGE (e:Endpoint {collection: $collection, path: row.path})
                MERGE (ch)-[:DECLARES_ENDPOINT]->(e)
                """,
                collection=collection,
                rows=rows,
            )

        for slice_start in range(0, len(batch.http_calls), self._settings.graph_writer_batch):
            rows = batch.http_calls[slice_start : slice_start + self._settings.graph_writer_batch]
            await session.run(
                """
                UNWIND $rows AS row
                MATCH (ch:Chunk {chunk_id: row.chunk_id})
                MERGE (e:Endpoint {collection: $collection, path: row.path})
                MERGE (ch)-[:HTTP_CALLS]->(e)
                """,
                collection=collection,
                rows=rows,
            )

        for slice_start in range(0, len(batch.configures), self._settings.graph_writer_batch):
            rows = batch.configures[slice_start : slice_start + self._settings.graph_writer_batch]
            await session.run(
                """
                UNWIND $rows AS row
                MATCH (ch:Chunk {chunk_id: row.chunk_id})
                MERGE (e:Endpoint {collection: $collection, path: row.path})
                MERGE (ch)-[:CONFIGURES]->(e)
                """,
                collection=collection,
                rows=rows,
            )

        for slice_start in range(0, len(batch.build_deps), self._settings.graph_writer_batch):
            rows = batch.build_deps[slice_start : slice_start + self._settings.graph_writer_batch]
            await session.run(
                """
                UNWIND $rows AS row
                MATCH (col:Collection {name: $collection})
                MERGE (a:Artifact {key: row.key})
                SET a.name = row.name,
                    a.group = row.group,
                    a.ecosystem = row.ecosystem,
                    a.version = row.version,
                    a.scope = row.scope
                MERGE (col)-[:BUILD_DEPENDS]->(a)
                """,
                collection=collection,
                rows=rows,
            )

        for slice_start in range(0, len(batch.resolves_to), self._settings.graph_writer_batch):
            rows = batch.resolves_to[slice_start : slice_start + self._settings.graph_writer_batch]
            await session.run(
                """
                UNWIND $rows AS row
                MATCH (a:Artifact {key: row.artifact_key})
                MERGE (col:Collection {name: row.target_collection})
                MERGE (a)-[:RESOLVES_TO]->(col)
                """,
                rows=rows,
            )
