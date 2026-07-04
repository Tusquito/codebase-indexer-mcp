"""Neo4j call-site parity via Testcontainers (opt-in when Docker available)."""

from __future__ import annotations

import pytest

pytest.importorskip("testcontainers")

from testcontainers.neo4j import Neo4jContainer  # noqa: E402

from codebase_indexer.config import Settings  # noqa: E402
from codebase_indexer.indexer.chunker import chunk_file  # noqa: E402
from codebase_indexer.indexer.graph_writer import write_chunks_to_graph  # noqa: E402
from codebase_indexer.storage.neo4j import Neo4jStorage  # noqa: E402
from codebase_indexer.tools.cross_references import UrlExtractors  # noqa: E402

from tests import test_cross_references as tcr  # noqa: E402


@pytest.mark.slow
@pytest.mark.asyncio
async def test_neo4j_find_callers_matches_fixture_tokens():
    """CALLS.call_token lookup returns the same callers as Qdrant callees scroll."""
    indexed, callees_map = tcr._indexed_java_call_site_fixtures()

    with Neo4jContainer("neo4j:5.26-community") as neo4j:
        settings = Settings(
            dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
            sparse_embed_model="Qdrant/bm25",
            dense_embed_vector_size=768,
            sparse_threads=2,
            graph_enabled=True,
            neo4j_uri=neo4j.get_connection_url(),
            neo4j_password=neo4j.password,
        )
        storage = Neo4jStorage(settings)
        await storage.ensure_schema()

        fixtures = [
            (tcr._JAVA_ABSTRACT_UDH, "AbstractUdhBusinessService.java"),
            (tcr._JAVA_CREATE_TIE, "CreateTieBusinessService.java"),
            (tcr._JAVA_LOGIN, "LoginBusinessService.java"),
            (tcr._JAVA_OTHER_CALLER, "OtherCaller.java"),
        ]
        chunks = []
        for source, rel_path in fixtures:
            chunks.extend(chunk_file(source, rel_path, "java", "fixture"))

        await write_chunks_to_graph(
            storage,
            collection="udh",
            chunks=chunks,
            url_extractors=UrlExtractors(),
            workspace_path="/workspace",
            collection_names=["udh"],
        )

        def _qdrant_ids(method: str, receiver: str | None) -> set[str]:
            token = f"{receiver}.{method}" if receiver else method
            return {
                r.chunk_id
                for r in indexed
                if token in callees_map.get(r.chunk_id, [])
            }

        for receiver in (None, "featureManagmentService"):
            qdrant_ids = _qdrant_ids("isEnabled", receiver)
            neo4j_results = await storage.find_callers(
                method="isEnabled",
                collections=["udh"],
                receiver=receiver,
            )
            neo4j_ids = {r.chunk_id for r in neo4j_results}
            assert neo4j_ids == qdrant_ids

        await storage.close()
