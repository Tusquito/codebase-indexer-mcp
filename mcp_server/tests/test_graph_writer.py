"""Unit tests for index-time graph writer."""

from codebase_indexer.indexer.chunker import Chunk
from codebase_indexer.indexer.graph_writer import (
    artifact_key,
    build_graph_batch,
    extract_file_import_names,
    import_qualified_name,
    symbol_qualified_name,
)
from codebase_indexer.tools.cross_references import UrlExtractors


def test_extract_imported_names_public_api():
    names = extract_file_import_names(
        "from foo.bar import Baz\nimport os\n",
        "python",
    )
    assert "Baz" in names
    assert "os" in names


def test_symbol_qualified_name_format():
    qn = symbol_qualified_name("coll", "src/a.py", "MyClass")
    assert qn == "coll:src/a.py::MyClass"


def test_build_graph_batch_from_chunks():
    extractors = UrlExtractors(["api", "rest"])
    chunks = [
        Chunk(
            chunk_id="c1",
            content='@GetMapping("/api/users")\npublic void getUsers() { client.get("/api/profile"); }',
            rel_path="demo/src/UserController.java",
            language="java",
            start_line=1,
            end_line=3,
            symbol_name="getUsers",
            symbol_type="method",
            file_sha256="sha",
            callees=["get"],
        )
    ]

    batch = build_graph_batch(
        collection="demo",
        chunks=chunks,
        url_extractors=extractors,
        workspace_path="/workspace",
        collection_names=["demo", "other"],
        schema_version=1,
    )

    assert batch.collection == "demo"
    assert len(batch.files) == 1
    assert len(batch.chunks) == 1
    assert any(d["name"] == "getUsers" for d in batch.defines)
    assert any(c["name"] == "get" for c in batch.calls)
    assert batch.declares_endpoint or batch.http_calls


def test_build_graph_batch_build_manifest(tmp_path):
    manifest = tmp_path / "demo" / "pom.xml"
    manifest.parent.mkdir(parents=True)
    manifest.write_text(
        """
        <project>
          <dependency>
            <groupId>com.example</groupId>
            <artifactId>other-service</artifactId>
            <version>1.0</version>
          </dependency>
        </project>
        """,
        encoding="utf-8",
    )
    extractors = UrlExtractors()
    chunks = [
        Chunk(
            chunk_id="m1",
            content=manifest.read_text(encoding="utf-8"),
            rel_path="demo/pom.xml",
            language="other",
            start_line=1,
            end_line=10,
            symbol_name=None,
            symbol_type="other",
            file_sha256="sha",
        )
    ]

    batch = build_graph_batch(
        collection="demo",
        chunks=chunks,
        url_extractors=extractors,
        workspace_path=str(tmp_path),
        collection_names=["demo", "other-service"],
        schema_version=1,
    )

    assert len(batch.build_deps) == 1
    assert batch.build_deps[0]["name"] == "other-service"
    assert any(r["target_collection"] == "other-service" for r in batch.resolves_to)


def test_import_qualified_name_stable():
    assert import_qualified_name("demo", "requests") == "demo::import::requests"


def test_artifact_key_includes_ecosystem():
    key = artifact_key("com.example", "svc", "maven")
    assert key == "maven:com.example:svc"
