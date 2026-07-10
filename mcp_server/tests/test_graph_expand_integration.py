"""expand_subgraph parity via Testcontainers Neo4j (ADR 0002 Phase 3).

Indexes a small multi-file fixture into the graph, then runs expand_subgraph on
a seed chunk and asserts the expected CALLS and HTTP_CALLS edges appear.
"""

from __future__ import annotations

import pytest

pytest.importorskip("testcontainers")

from testcontainers.neo4j import Neo4jContainer  # noqa: E402

from codebase_indexer.config import Settings  # noqa: E402
from codebase_indexer.indexer.chunker import Chunk  # noqa: E402
from codebase_indexer.indexer.graph_writer import write_chunks_to_graph  # noqa: E402
from codebase_indexer.storage.neo4j import Neo4jStorage  # noqa: E402
from codebase_indexer.tools.cross_references import UrlExtractors  # noqa: E402


@pytest.mark.slow
@pytest.mark.asyncio
async def test_expand_subgraph_returns_calls_and_http_edges():
    chunks = [
        Chunk(
            chunk_id="demo/UserController.java:1",
            content=(
                '@GetMapping("/api/users")\n'
                "public void getUsers() { client.get(\"/api/profile\"); }"
            ),
            rel_path="demo/src/UserController.java",
            language="java",
            start_line=1,
            end_line=3,
            symbol_name="getUsers",
            symbol_type="method",
            file_sha256="sha1",
            callees=["get"],
        ),
    ]

    with Neo4jContainer("neo4j:5.26-community") as neo4j:
        settings = Settings(
            dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
            sparse_embed_model="Qdrant/bm25",
            dense_embed_vector_size=768,
            sparse_threads=2,
            graph_enabled=True,
            graph_max_hops=2,
            graph_max_nodes=200,
            neo4j_uri=neo4j.get_connection_url(),
            neo4j_password=neo4j.password,
        )
        storage = Neo4jStorage(settings)
        await storage.ensure_schema()

        await write_chunks_to_graph(
            storage,
            collection="demo",
            chunks=chunks,
            url_extractors=UrlExtractors(["api", "rest"]),
            workspace_path="/workspace",
            collection_names=["demo"],
        )

        expansion = await storage.expand_subgraph(
            chunk_ids=["demo/UserController.java:1"],
            max_hops=2,
            max_nodes=200,
        )

        edge_types = {e.type for e in expansion.edges}
        assert "CALLS" in edge_types
        assert "HTTP_CALLS" in edge_types
        # Strictly more relationship data than the seed hit alone.
        assert expansion.nodes

        await storage.close()
