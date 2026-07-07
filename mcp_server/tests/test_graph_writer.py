"""Unit tests for index-time graph writer."""

from codebase_indexer.indexer.chunker import Chunk
from codebase_indexer.indexer.graph_writer import (
    GraphBatch,
    artifact_key,
    build_graph_batch,
    callee_qualified_name,
    extract_file_import_names,
    graph_node_ids_from_batch,
    import_qualified_name,
    resolve_call_target,
    symbol_qualified_name,
    _DefineEntry,
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
    )

    assert batch.collection == "demo"
    assert len(batch.files) == 1
    assert len(batch.chunks) == 1
    assert any(d["name"] == "getUsers" for d in batch.defines)
    assert any(c["name"] == "get" and c["call_token"] == "get" for c in batch.calls)
    assert batch.declares_endpoint or batch.http_calls


def test_graph_node_ids_from_batch_neighbor_keys_only():
    batch = GraphBatch(collection="demo")
    batch.chunks = [
        {"chunk_id": "c1", "rel_path": "a.py", "start_line": 1, "end_line": 5},
        {"chunk_id": "c2", "rel_path": "a.py", "start_line": 6, "end_line": 9},
    ]
    batch.defines = [
        {"chunk_id": "c1", "qualified_name": "demo:a.py::foo", "name": "foo", "kind": "function"},
    ]
    batch.calls = [
        {"chunk_id": "c1", "qualified_name": "demo::callee::bar", "name": "bar", "call_token": "bar"},
        # duplicate CALLS to same target must be deduped
        {"chunk_id": "c1", "qualified_name": "demo::callee::bar", "name": "bar", "call_token": "bar"},
    ]
    batch.declares_endpoint = [{"chunk_id": "c1", "path": "/api/users"}]
    batch.http_calls = [{"chunk_id": "c2", "path": "/api/profile"}]
    batch.imports = [
        {"rel_path": "a.py", "qualified_name": "demo::import::os", "name": "os"},
    ]

    mapping = graph_node_ids_from_batch(batch)

    # c1: own define symbol, call target, endpoint, file-level import
    assert mapping["c1"] == [
        "demo:a.py::foo",
        "demo::callee::bar",
        "demo:/api/users",
        "demo::import::os",
    ]
    # c2: http_calls endpoint + file-level import (imports apply to all file chunks)
    assert mapping["c2"] == ["demo:/api/profile", "demo::import::os"]
    # No raw chunk_id (own Chunk key) leaked into neighbor lists
    for keys in mapping.values():
        assert "c1" not in keys and "c2" not in keys


def test_graph_node_ids_from_batch_empty():
    assert graph_node_ids_from_batch(GraphBatch(collection="demo")) == {}


def test_resolve_call_target_unifies_unique_method_name():
    defines = {
        "isEnabled": [
            _DefineEntry(
                qualified_name="demo:Feature.java::isEnabled",
                rel_path="Feature.java",
                name="isEnabled",
                kind="method",
            )
        ]
    }
    qn, name = resolve_call_target("isEnabled", "demo", "Caller.java", defines, [])
    assert qn == "demo:Feature.java::isEnabled"
    assert name == "isEnabled"


def test_resolve_call_target_stub_when_ambiguous():
    defines = {
        "run": [
            _DefineEntry("demo:a.py::run", "a.py", "run", "function"),
            _DefineEntry("demo:b.py::run", "b.py", "run", "function"),
        ]
    }
    qn, name = resolve_call_target("run", "demo", "Caller.py", defines, [])
    assert qn == callee_qualified_name("demo", "run")
    assert name == "run"


def test_resolve_call_target_qualified_import_fallback():
    defines = {
        "save": [
            _DefineEntry(
                qualified_name="demo:OrderRepo.java::save",
                rel_path="OrderRepo.java",
                name="save",
                kind="method",
            ),
            _DefineEntry(
                qualified_name="demo:UserRepo.java::save",
                rel_path="UserRepo.java",
                name="save",
                kind="method",
            ),
        ]
    }
    qn, name = resolve_call_target(
        "orderRepo.save",
        "demo",
        "Service.java",
        defines,
        ["orderRepo"],
    )
    assert qn == "demo:OrderRepo.java::save"
    assert name == "save"


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
    )

    assert len(batch.build_deps) == 1
    assert batch.build_deps[0]["name"] == "other-service"
    assert any(r["target_collection"] == "other-service" for r in batch.resolves_to)


def test_import_qualified_name_stable():
    assert import_qualified_name("demo", "requests") == "demo::import::requests"


def test_artifact_key_includes_ecosystem():
    key = artifact_key("com.example", "svc", "maven")
    assert key == "maven:com.example:svc"
