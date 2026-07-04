"""Unit tests for cross-reference path matching and classification."""

from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock

import pytest
from fastmcp import FastMCP

from codebase_indexer.indexer.chunker import chunk_file
from codebase_indexer.indexer.embedder import SparseVector
from codebase_indexer.storage.qdrant import SearchResult
from codebase_indexer.tools.cross_references import (
    UrlExtractors,
    _build_link_summary,
    _build_url_extractors,
    _classify_reference,
    _extract_code_urls,
    _extract_route_paths,
    _paths_match,
    configure_url_keywords,
    register_cross_references_tool,
)


def test_paths_match_partial_segments():
    assert _paths_match("/profile/me/email/{id}", "me/email") is True
    assert _paths_match("/rest/login", "rest/login") is True


def test_paths_match_requires_two_segments():
    # Single-segment paths must not match to avoid false positives.
    assert _paths_match("/login", "login") is False
    assert _paths_match("app", "x") is False


def test_paths_match_rejects_unrelated():
    assert _paths_match("/orders/list", "users/profile") is False


def test_extract_route_paths_spring_and_aspnet():
    assert "foo/bar" in _extract_route_paths('@GetMapping("/foo/bar")')
    assert "me/email" in _extract_route_paths('[Route("me/email")]')


def test_extract_route_paths_controller_token():
    paths = _extract_route_paths('[Route("[controller]/list")]', "src/EmailController.cs")
    assert any("email" in p for p in paths)


def test_classify_reference_variants():
    assert _classify_reference("import os\nx = 1", "os") == "import"
    assert _classify_reference('@GetMapping("/x/y")', "y") == "endpoint_definition"
    assert _classify_reference("var c = new HttpClient();", "HttpClient") == "http_call"
    assert _classify_reference("class Foo {}", "Foo") == "definition"
    assert _classify_reference("Foo.doThing()", "Foo") == "usage"


def test_configure_url_keywords_changes_extraction():
    configure_url_keywords(["widgets"])
    assert _extract_code_urls('"/widgets/list/all"') == ["widgets/list/all"]
    # Restore generic defaults so other tests are unaffected.
    configure_url_keywords([])


def test_classify_reference_build_dependency():
    pom_content = """
    <dependency>
        <groupId>com.example.contracts</groupId>
        <artifactId>myapp-contracts-definitions</artifactId>
        <version>2.1.0-SNAPSHOT</version>
    </dependency>
    """
    assert _classify_reference(pom_content, "myapp-contracts-definitions", "pom.xml") == "build_dependency"


def test_classify_reference_build_dependency_csproj():
    csproj_content = '<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />'
    assert _classify_reference(csproj_content, "Newtonsoft.Json", "MyApp.csproj") == "build_dependency"

    config_extractors, code_extractors = _build_url_extractors(["api"])
    assert config_extractors and code_extractors
    # /v2/ should match via the always-on version segment, not the keyword.
    assert code_extractors[0].search('"/v2/users/me"') is not None


# ---------------------------------------------------------------------------
# Call-site path (Path D) — helpers mirroring indexed callees scroll filter
# ---------------------------------------------------------------------------

_JAVA_ABSTRACT_UDH = """\
package com.example;

public abstract class AbstractUdhBusinessService {
    protected FeatureManagmentService featureManagmentService;
}
"""

_JAVA_CREATE_TIE = """\
package com.example;

public class CreateTieBusinessService extends AbstractUdhBusinessService {
    public void createTie(String flag) {
        if (featureManagmentService.isEnabled(flag)) {
            doWork();
        }
    }
}
"""

_JAVA_LOGIN = """\
package com.example;

public class LoginBusinessService extends AbstractUdhBusinessService {
    public void login() {
        doLogin();
    }
}
"""

_JAVA_OTHER_CALLER = """\
package com.example;

public class OtherCaller {
    public void run() {
        other.isEnabled(flag);
    }
}
"""


def _indexed_java_call_site_fixtures() -> tuple[list[SearchResult], dict[str, list[str]]]:
    """Build SearchResult rows and a chunk_id → callees map from Java fixtures."""
    fixtures = [
        (_JAVA_ABSTRACT_UDH, "AbstractUdhBusinessService.java"),
        (_JAVA_CREATE_TIE, "CreateTieBusinessService.java"),
        (_JAVA_LOGIN, "LoginBusinessService.java"),
        (_JAVA_OTHER_CALLER, "OtherCaller.java"),
    ]
    results: list[SearchResult] = []
    callees_by_chunk_id: dict[str, list[str]] = {}
    for source, rel_path in fixtures:
        for chunk in chunk_file(source, rel_path, "java", "fixture"):
            callees_by_chunk_id[chunk.chunk_id] = chunk.callees
            results.append(
                SearchResult(
                    chunk_id=chunk.chunk_id,
                    score=0.0,
                    rel_path=chunk.rel_path,
                    language=chunk.language,
                    start_line=chunk.start_line,
                    end_line=chunk.end_line,
                    symbol_name=chunk.symbol_name,
                    symbol_type=chunk.symbol_type,
                    content=chunk.content,
                    collection="udh",
                )
            )
    return results, callees_by_chunk_id


async def _setup_find_cross_references(
    indexed_results: list[SearchResult],
    callees_by_chunk_id: dict[str, list[str]],
    *,
    graph_storage=None,
):
    """Register find_cross_references with a fake storage scroll on callees."""
    async def find_callers_in_collections(
        method: str,
        collections: list[str],
        receiver: str | None = None,
        limit_per_collection: int = 10,
    ) -> list[SearchResult]:
        token = f"{receiver}.{method}" if receiver else method
        matched = [
            r
            for r in indexed_results
            if r.collection in collections and token in callees_by_chunk_id.get(r.chunk_id, [])
        ]
        return matched[: limit_per_collection * len(collections)]

    async def find_callers_graph(
        method: str,
        collections: list[str],
        receiver: str | None = None,
        limit_per_collection: int = 10,
    ) -> list[SearchResult]:
        return await find_callers_in_collections(
            method, collections, receiver=receiver, limit_per_collection=limit_per_collection
        )

    storage = AsyncMock()
    storage.list_collection_stats = AsyncMock(return_value=[])
    storage.search = AsyncMock(return_value=[])
    storage.find_symbol_in_collections = AsyncMock(return_value=[])
    storage.find_callers_in_collections = AsyncMock(side_effect=find_callers_in_collections)

    if graph_storage is not None:
        graph_storage.find_callers = AsyncMock(side_effect=find_callers_graph)
        graph_storage.enabled = True

    embedder = MagicMock()
    embedder.embed_query = AsyncMock(return_value=([], None, None))

    ctx = SimpleNamespace(
        storage=storage,
        embedder=embedder,
        url_extractors=UrlExtractors(),
        graph_storage=graph_storage,
    )

    mcp = FastMCP("test")
    register_cross_references_tool(mcp, ctx)
    tool = await mcp.get_tool("find_cross_references")
    return tool.fn, storage, graph_storage


def _call_site_symbol_names(found_in: dict) -> set[str]:
    return {
        row["symbol_name"]
        for rows in found_in.values()
        for row in rows
        if row.get("match_type") == "call_site"
    }


@pytest.mark.asyncio
async def test_find_cross_references_call_site_path_excludes_passive_inheritor():
    indexed, callees_map = _indexed_java_call_site_fixtures()
    find_cross_references, storage, _ = await _setup_find_cross_references(indexed, callees_map)

    result = await find_cross_references(
        symbol_name="isEnabled",
        member="isEnabled",
        collections=["udh"],
    )

    call_sites = _call_site_symbol_names(result["found_in"])
    assert "CreateTieBusinessService" in call_sites
    assert "OtherCaller" in call_sites
    assert "LoginBusinessService" not in call_sites
    assert "AbstractUdhBusinessService" not in call_sites

    for rows in result["found_in"].values():
        for row in rows:
            if row.get("match_type") == "call_site":
                assert row["reference_type"] == "call_site"

    storage.find_callers_in_collections.assert_awaited_once_with(
        method="isEnabled",
        collections=["udh"],
        receiver=None,
        limit_per_collection=10,
    )


@pytest.mark.asyncio
async def test_find_cross_references_call_site_receiver_qualifier_excludes_other_receiver():
    indexed, callees_map = _indexed_java_call_site_fixtures()
    find_cross_references, storage, _ = await _setup_find_cross_references(indexed, callees_map)

    result = await find_cross_references(
        symbol_name="isEnabled",
        member="isEnabled",
        receiver="featureManagmentService",
        collections=["udh"],
    )

    call_sites = _call_site_symbol_names(result["found_in"])
    assert call_sites == {"CreateTieBusinessService"}

    storage.find_callers_in_collections.assert_awaited_once_with(
        method="isEnabled",
        collections=["udh"],
        receiver="featureManagmentService",
        limit_per_collection=10,
    )


@pytest.mark.asyncio
async def test_find_cross_references_path_d_routes_neo4j_when_graph_enabled():
    indexed, callees_map = _indexed_java_call_site_fixtures()
    graph_storage = SimpleNamespace(enabled=True)
    find_cross_references, storage, graph_storage = await _setup_find_cross_references(
        indexed, callees_map, graph_storage=graph_storage
    )

    result = await find_cross_references(
        symbol_name="isEnabled",
        member="isEnabled",
        collections=["udh"],
    )

    call_sites = _call_site_symbol_names(result["found_in"])
    assert "CreateTieBusinessService" in call_sites
    storage.find_callers_in_collections.assert_not_awaited()
    graph_storage.find_callers.assert_awaited_once_with(
        method="isEnabled",
        collections=["udh"],
        receiver=None,
        limit_per_collection=10,
    )


@pytest.mark.asyncio
async def test_find_cross_references_path_d_qdrant_when_graph_disabled():
    indexed, callees_map = _indexed_java_call_site_fixtures()
    find_cross_references, storage, _ = await _setup_find_cross_references(
        indexed, callees_map, graph_storage=None
    )

    await find_cross_references(
        symbol_name="isEnabled",
        member="isEnabled",
        collections=["udh"],
    )

    storage.find_callers_in_collections.assert_awaited_once()


@pytest.mark.asyncio
async def test_call_site_parity_qdrant_vs_neo4j_tokens():
    """Neo4j call_token lookup must return the same caller set as Qdrant callees scroll."""
    from contextlib import asynccontextmanager

    from codebase_indexer.config import Settings
    from codebase_indexer.storage.neo4j import Neo4jStorage

    indexed, callees_map = _indexed_java_call_site_fixtures()

    def _qdrant_ids(method: str, receiver: str | None) -> set[str]:
        token = f"{receiver}.{method}" if receiver else method
        return {
            r.chunk_id
            for r in indexed
            if token in callees_map.get(r.chunk_id, [])
        }

    class _ParityResult:
        def __init__(self, records: list[dict]) -> None:
            self._records = records

        async def data(self) -> list[dict]:
            return self._records

    class _ParitySession:
        def __init__(self) -> None:
            self.last_token: str | None = None

        async def run(self, query: str, **params):
            self.last_token = params["tokens"][0]
            records = [
                {
                    "chunk_id": r.chunk_id,
                    "rel_path": r.rel_path,
                    "start_line": r.start_line,
                    "end_line": r.end_line,
                    "language": r.language,
                    "symbol_name": r.symbol_name,
                    "symbol_type": r.symbol_type,
                }
                for r in indexed
                if self.last_token in callees_map.get(r.chunk_id, [])
            ]
            return _ParityResult(records)

    class _ParityDriver:
        def __init__(self) -> None:
            self.session_obj = _ParitySession()

        @asynccontextmanager
        async def session(self, *, database: str):
            yield self.session_obj

    settings = Settings(
        dense_embed_model="nomic-ai/nomic-embed-text-v1.5",
        sparse_embed_model="Qdrant/bm25",
        dense_embed_vector_size=768,
        sparse_threads=2,
        graph_enabled=True,
        neo4j_password="secret",
    )
    storage = Neo4jStorage(settings, driver=_ParityDriver())  # type: ignore[arg-type]

    for receiver in (None, "featureManagmentService"):
        qdrant_ids = _qdrant_ids("isEnabled", receiver)
        neo4j_results = await storage.find_callers(
            method="isEnabled",
            collections=["udh"],
            receiver=receiver,
        )
        neo4j_ids = {r.chunk_id for r in neo4j_results}
        assert neo4j_ids == qdrant_ids


@pytest.mark.asyncio
async def test_find_cross_references_semantic_path_passes_colbert_vector():
    colbert = [[0.1, 0.2], [0.3, 0.4]]
    storage = AsyncMock()
    storage.list_collection_stats = AsyncMock(return_value=[])
    storage.search = AsyncMock(return_value=[])
    storage.find_symbol_in_collections = AsyncMock(return_value=[])
    storage.find_callers_in_collections = AsyncMock(return_value=[])

    embedder = MagicMock()
    embedder.embed_query = AsyncMock(
        return_value=([0.5], SparseVector(indices=[1], values=[1.0]), colbert)
    )

    ctx = SimpleNamespace(
        storage=storage,
        embedder=embedder,
        url_extractors=UrlExtractors(),
        graph_storage=None,
    )

    mcp = FastMCP("test")
    register_cross_references_tool(mcp, ctx)
    find_cross_references = (await mcp.get_tool("find_cross_references")).fn

    await find_cross_references(
        query="HTTP client RestTemplate",
        collections=["svc-a", "svc-b"],
    )

    storage.search.assert_awaited_once()
    assert storage.search.await_args.kwargs["colbert_vector"] == colbert


@pytest.mark.asyncio
async def test_find_cross_references_rerank_false_skips_colbert_on_semantic_paths():
    storage = AsyncMock()
    storage.list_collection_stats = AsyncMock(return_value=[])
    storage.search = AsyncMock(return_value=[])
    storage.find_symbol_in_collections = AsyncMock(return_value=[])
    storage.find_callers_in_collections = AsyncMock(return_value=[])

    embedder = MagicMock()
    embedder.embed_query = AsyncMock(return_value=([0.5], SparseVector(indices=[1], values=[1.0]), None))

    ctx = SimpleNamespace(
        storage=storage,
        embedder=embedder,
        url_extractors=UrlExtractors(),
        graph_storage=None,
    )

    mcp = FastMCP("test")
    register_cross_references_tool(mcp, ctx)
    find_cross_references = (await mcp.get_tool("find_cross_references")).fn

    await find_cross_references(
        query="HTTP client RestTemplate",
        symbol_name="MyService",
        collections=["svc-a", "svc-b"],
        rerank=False,
    )

    assert embedder.embed_query.await_count == 2
    for call in embedder.embed_query.await_args_list:
        assert call.kwargs["rerank"] is False
    for call in storage.search.await_args_list:
        assert call.kwargs["colbert_vector"] is None


def test_build_link_summary_links_call_site_to_definition_same_collection():
    extractors = UrlExtractors()
    by_collection = {
        "udh": [
            {
                "rel_path": "CreateTieBusinessService.java",
                "symbol_name": "CreateTieBusinessService",
                "reference_type": "call_site",
                "content": "featureManagmentService.isEnabled(flag)",
            },
            {
                "rel_path": "FeatureManagmentService.java",
                "symbol_name": "isEnabled",
                "reference_type": "definition",
                "content": "public boolean isEnabled(String flag) { return true; }",
            },
        ],
    }

    links = _build_link_summary(by_collection, extractors, member="isEnabled")

    assert len(links) == 1
    assert links[0]["type"] == "code_dependency"
    assert links[0]["from"]["reference_type"] == "call_site"
    assert links[0]["from"]["path"] == "CreateTieBusinessService.java"
    assert links[0]["to"]["symbol"] == "isEnabled"
