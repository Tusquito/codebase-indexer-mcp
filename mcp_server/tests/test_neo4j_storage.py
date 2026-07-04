"""Unit tests for Neo4jStorage with a mock async driver."""

from __future__ import annotations

from contextlib import asynccontextmanager

import pytest

from codebase_indexer.config import Settings
from codebase_indexer.indexer.graph_writer import GraphBatch
from codebase_indexer.storage.neo4j import Neo4jStorage


class _MockResult:
    def __init__(self, records: list[dict] | None = None) -> None:
        self._records = records or []

    async def data(self) -> list[dict]:
        return self._records


class _MockSession:
    def __init__(self) -> None:
        self.queries: list[tuple[str, dict]] = []

    async def run(self, query: str, **params):
        self.queries.append((query.strip(), params))
        if "r.call_token" in query:
            return _MockResult(
                [
                    {
                        "chunk_id": "chunk-caller",
                        "rel_path": "CreateTie.java",
                        "start_line": 3,
                        "end_line": 7,
                        "language": "java",
                        "symbol_name": "createTie",
                        "symbol_type": "method",
                    }
                ]
            )
        return _MockResult()


class _MockDriver:
    def __init__(self) -> None:
        self.session_obj = _MockSession()
        self.closed = False

    @asynccontextmanager
    async def session(self, *, database: str):
        yield self.session_obj

    async def close(self) -> None:
        self.closed = True


def _graph_settings(**overrides) -> Settings:
    base = dict(
        dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=768,
        sparse_threads=2,
        graph_enabled=True,
        neo4j_password="secret",
        graph_writer_batch=500,
    )
    base.update(overrides)
    return Settings(**base)


def test_neo4j_storage_disabled_skips_driver():
    settings = Settings()
    storage = Neo4jStorage(settings)
    assert storage.enabled is False
    assert storage._driver is None


@pytest.mark.asyncio
async def test_ensure_schema_runs_constraints():
    driver = _MockDriver()
    storage = Neo4jStorage(_graph_settings(), driver=driver)  # type: ignore[arg-type]

    await storage.ensure_schema()

    assert storage._schema_ready is True
    assert len(driver.session_obj.queries) >= 7
    assert any("chunk_id_unique" in q for q, _ in driver.session_obj.queries)
    assert any("calls_call_token" in q for q, _ in driver.session_obj.queries)


@pytest.mark.asyncio
async def test_delete_files_runs_cypher():
    driver = _MockDriver()
    storage = Neo4jStorage(_graph_settings(), driver=driver)  # type: ignore[arg-type]

    await storage.delete_files("myproj", ["src/a.py", "src/b.py"])

    assert len(driver.session_obj.queries) == 1
    query, params = driver.session_obj.queries[0]
    assert "DETACH DELETE" in query
    assert params["collection"] == "myproj"
    assert params["paths"] == ["src/a.py", "src/b.py"]


@pytest.mark.asyncio
async def test_write_batch_merges_collection_and_chunks():
    driver = _MockDriver()
    storage = Neo4jStorage(_graph_settings(), driver=driver)  # type: ignore[arg-type]
    batch = GraphBatch(
        collection="demo",
        files=[{"rel_path": "main.py", "language": "python", "sha256": "abc"}],
        chunks=[
            {
                "chunk_id": "chunk-1",
                "rel_path": "main.py",
                "start_line": 1,
                "end_line": 10,
            }
        ],
        defines=[
            {
                "chunk_id": "chunk-1",
                "qualified_name": "demo:main.py::hello",
                "name": "hello",
                "kind": "function",
            }
        ],
    )

    await storage.write_batch(batch)

    combined = " ".join(q for q, _ in driver.session_obj.queries)
    assert "MERGE (col:Collection" in combined
    assert "MERGE (ch:Chunk" in combined
    assert "MERGE (s:Symbol" in combined


@pytest.mark.asyncio
async def test_write_batch_calls_sets_call_token():
    driver = _MockDriver()
    storage = Neo4jStorage(_graph_settings(), driver=driver)  # type: ignore[arg-type]
    batch = GraphBatch(
        collection="demo",
        files=[{"rel_path": "main.py", "language": "python", "sha256": "abc"}],
        chunks=[
            {
                "chunk_id": "chunk-1",
                "rel_path": "main.py",
                "start_line": 1,
                "end_line": 10,
            }
        ],
        calls=[
            {
                "chunk_id": "chunk-1",
                "qualified_name": "demo::callee::foo",
                "name": "foo",
                "call_token": "foo",
            }
        ],
    )

    await storage.write_batch(batch)

    call_queries = [q for q, _ in driver.session_obj.queries if "CALLS" in q]
    assert call_queries
    assert any("call_token" in q for q in call_queries)


@pytest.mark.asyncio
async def test_find_callers_matches_call_token():
    driver = _MockDriver()
    storage = Neo4jStorage(_graph_settings(), driver=driver)  # type: ignore[arg-type]

    results = await storage.find_callers(
        method="isEnabled",
        collections=["udh"],
        receiver="featureManagmentService",
        limit_per_collection=5,
    )

    assert len(results) == 1
    assert results[0].chunk_id == "chunk-caller"
    assert results[0].collection == "udh"
    query, params = next((q, p) for q, p in driver.session_obj.queries if "r.call_token" in q)
    assert params["tokens"] == ["featureManagmentService.isEnabled"]
    assert params["limit"] == 5


@pytest.mark.asyncio
async def test_close_closes_driver():
    driver = _MockDriver()
    storage = Neo4jStorage(_graph_settings(), driver=driver)  # type: ignore[arg-type]

    await storage.close()

    assert driver.closed is True
    assert storage._driver is None
