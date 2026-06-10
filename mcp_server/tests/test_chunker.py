"""Unit tests for the tree-sitter / sliding-window chunker."""

from codebase_indexer.indexer.chunker import (
    _classify_file_symbol_type,
    _extract_imported_names,
    _filter_relevant_imports,
    chunk_file,
)


PY_SAMPLE = '''\
def foo():
    return 1


class Bar:
    def baz(self):
        return 2
'''


def test_chunk_python_extracts_symbols():
    chunks = chunk_file(PY_SAMPLE, "sample.py", "python", "deadbeef")
    names = {c.symbol_name for c in chunks}
    assert "foo" in names
    assert "Bar" in names
    # Every chunk carries the file hash and 1-based line numbers.
    assert all(c.file_sha256 == "deadbeef" for c in chunks)
    assert all(c.start_line >= 1 for c in chunks)


def test_chunk_empty_returns_nothing():
    assert chunk_file("", "empty.py", "python", "x") == []


def test_chunk_unsupported_language_uses_sliding_window():
    content = "\n".join(f"line {i}" for i in range(200))
    chunks = chunk_file(content, "notes.md", "markdown", "x", max_chunk_lines=60)
    assert len(chunks) >= 2
    assert all(c.symbol_type == "other" for c in chunks)


def test_pom_xml_sliding_window_with_manifest_type():
    content = '<?xml version="1.0"?>\n<project>\n  <artifactId>demo</artifactId>\n</project>\n'
    chunks = chunk_file(content, "udh-adpt/pom.xml", "xml", "abc", max_chunk_lines=60)
    assert chunks
    assert all(c.symbol_type == "manifest" for c in chunks)


def test_chunk_ids_are_deterministic():
    a = chunk_file(PY_SAMPLE, "sample.py", "python", "x")
    b = chunk_file(PY_SAMPLE, "sample.py", "python", "x")
    assert [c.chunk_id for c in a] == [c.chunk_id for c in b]


def test_selective_import_header_python():
    """Only imports whose symbols appear in a chunk are prepended."""
    source = '''\
import os
from typing import List

def greet(name: str) -> str:
    return f"Hello {name}"


class Greeter:
    def say_hello(self) -> None:
        os.getcwd()
'''
    chunks = chunk_file(source, "greet.py", "python", "abc")
    greet = next(c for c in chunks if c.symbol_name == "greet")
    greeter = next(c for c in chunks if c.symbol_name == "Greeter")

    assert "import os" not in greet.content
    assert "from typing import List" not in greet.content

    assert "import os" in greeter.content
    assert "from typing import List" not in greeter.content


def test_selective_import_header_java():
    """Java class chunks only prepend imports for types used in that class."""
    source = '''\
package com.example;

import com.udh.interface.UserService;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Assertions;

public class ListUsers {
    public List<String> getUsers(UserService userService) {
        return userService.findAll();
    }
}

public class DeleteUsers {
    public void deleteUser(UserService userService) {
        Assertions.assertNotNull(userService);
    }
}
'''
    chunks = chunk_file(source, "Users.java", "java", "abc")
    list_users = next(c for c in chunks if c.symbol_name == "ListUsers")
    delete_users = next(c for c in chunks if c.symbol_name == "DeleteUsers")

    assert "package com.example" in list_users.content
    assert "import com.udh.interface.UserService" in list_users.content
    assert "import java.util.List" in list_users.content
    assert "import java.util.Map" not in list_users.content
    assert "import org.junit.jupiter.api.Assertions" not in list_users.content

    assert "import com.udh.interface.UserService" in delete_users.content
    assert "import org.junit.jupiter.api.Assertions" in delete_users.content
    assert "import java.util.List" not in delete_users.content


def test_selective_import_header_csharp_filter():
    """C# usings are kept when any namespace segment is referenced in the chunk."""
    lines = ["using System;", "using System.IO;"]
    writer_body = "public IO.Stream Open() { return null; }"
    math_body = "return System.Math.Abs(value);"

    writer_imports = _filter_relevant_imports(lines, writer_body, "csharp")
    math_imports = _filter_relevant_imports(lines, math_body, "csharp")

    assert writer_imports == ["using System.IO;"]
    assert math_imports == ["using System;"]


def test_java_wildcard_import_always_prepended():
    source = '''\
package com.example;

import java.util.*;

public class Demo {
    public void work() {
        List<String> items = new ArrayList<>();
    }
}
'''
    chunks = chunk_file(source, "Demo.java", "java", "abc")
    demo = next(c for c in chunks if c.symbol_name == "Demo")
    assert "import java.util.*" in demo.content


def test_no_import_duplication_when_chunk_starts_with_imports():
    """Import header should not be prepended to chunks that already start with it."""
    source = '''\
import os


def foo():
    return os.getcwd()
'''
    chunks = chunk_file(source, "foo.py", "python", "abc")
    for chunk in chunks:
        assert chunk.content.count("import os") == 1, (
            f"Duplicate import in chunk: {chunk.symbol_name}"
        )


def test_extract_imported_names_java_alias_and_wildcard():
    assert _extract_imported_names("import java.util.*;", "java") is None
    assert _extract_imported_names("package com.foo;", "java") is None
    assert _extract_imported_names(
        "import com.udh.common.Action;", "java"
    ) == ["Action"]


def test_extract_imported_names_python_alias():
    assert _extract_imported_names("import numpy as np", "python") == ["np"]
    assert _extract_imported_names("from foo import bar, baz", "python") == [
        "bar",
        "baz",
    ]


def test_filter_relevant_imports_matches_symbols():
    lines = [
        "import com.udh.common.Action;",
        "import java.util.Map;",
    ]
    body = "customer = profile.get(Action.DELETE);"
    relevant = _filter_relevant_imports(lines, body, "java")
    assert len(relevant) == 1
    assert "Action" in relevant[0]


def test_go_grouped_import_lines_parsed():
    assert _extract_imported_names('    "fmt"', "go") == ["fmt"]
    assert _extract_imported_names('    "net/http"', "go") == ["http"]
    assert _extract_imported_names('http "net/http"', "go") == ["http"]


def test_go_grouped_imports_prepended_to_chunk():
    source = '''package main

import (
\t"fmt"
\t"net/http"
)

func handler(w http.ResponseWriter, r *http.Request) {
\tfmt.Fprintf(w, "Hello")
}
'''
    chunks = chunk_file(source, "main.go", "go", "abc")
    handler = next(c for c in chunks if c.symbol_name == "handler")
    assert '"fmt"' in handler.content
    assert '"net/http"' in handler.content


def test_rust_curly_brace_use_parsed():
    assert _extract_imported_names("use std::io::{Read, Write};", "rust") == [
        "Read",
        "Write",
    ]


def test_rust_curly_brace_use_filter():
    lines = ["use std::io::{Read, Write};"]
    body = "fn f(r: &mut Read) -> Result<()> { Ok(()) }"
    relevant = _filter_relevant_imports(lines, body, "rust")
    assert relevant == lines


def test_js_default_and_named_import_parsed():
    assert _extract_imported_names(
        "import React, { useState, useEffect } from 'react';",
        "javascript",
    ) == ["React", "useState", "useEffect"]


def test_js_default_and_named_import_filter():
    line = "import React, { useState, useEffect } from 'react';"
    body = "const [state, setState] = useState(0); return <React.Fragment />"
    relevant = _filter_relevant_imports([line], body, "javascript")
    assert line in relevant


def test_symbol_type_config_properties():
    source = "server.port=8080\ndb.url=jdbc:postgresql://localhost/db\n"
    chunks = chunk_file(source, "src/application.properties", "properties", "x")
    assert len(chunks) >= 1
    assert all(c.symbol_type == "config" for c in chunks)
    assert chunks[0].symbol_name == "application.properties"


def test_symbol_type_manifest_csproj():
    source = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup></PropertyGroup></Project>'
    chunks = chunk_file(source, "src/MyApp.csproj", "xml", "x")
    assert all(c.symbol_type == "manifest" for c in chunks)


def test_symbol_type_ops_dockerfile():
    source = "FROM node:20\nWORKDIR /app\nCOPY . .\n"
    chunks = chunk_file(source, "Dockerfile", "dockerfile", "x")
    assert all(c.symbol_type == "ops" for c in chunks)
    assert chunks[0].symbol_name == "Dockerfile"


def test_symbol_type_ops_github_workflow():
    source = "name: CI\non: [push]\njobs:\n  build:\n    runs-on: ubuntu-latest\n"
    chunks = chunk_file(source, ".github/workflows/ci.yml", "yaml", "x")
    assert all(c.symbol_type == "ops" for c in chunks)


def test_symbol_type_ops_azure_pipelines():
    source = "trigger:\n  - main\npool:\n  vmImage: ubuntu-latest\n"
    chunks = chunk_file(source, "azure-pipelines.yml", "yaml", "x")
    assert all(c.symbol_type == "ops" for c in chunks)


def test_symbol_type_config_generic_yaml():
    assert _classify_file_symbol_type("deploy/settings.yaml", "yaml") == "config"
    chunks = chunk_file("key: value\n", "deploy/settings.yaml", "yaml", "x")
    assert all(c.symbol_type == "config" for c in chunks)


def test_classify_manifest_by_filename():
    assert _classify_file_symbol_type("pom.xml", "xml") == "manifest"
    assert _classify_file_symbol_type("package.json", "json") == "manifest"


def test_symbol_type_config_dotenv():
    source = "DB_HOST=localhost\nDB_PORT=5432\n"
    chunks = chunk_file(source, ".env", "properties", "x")
    assert all(c.symbol_type == "config" for c in chunks)


def test_classify_config_compound_suffix_properties():
    assert (
        _classify_file_symbol_type("lib/desmon.client.properties", "properties")
        == "config"
    )


def test_symbol_type_config_compound_suffix_properties():
    source = "server.port=8080\n"
    chunks = chunk_file(source, "lib/desmon.client.properties", "properties", "x")
    assert len(chunks) >= 1
    assert all(c.symbol_type == "config" for c in chunks)
    assert chunks[0].symbol_name == "desmon.client.properties"


def test_classify_ops_build_pipeline_yaml():
    assert (
        _classify_file_symbol_type(
            "build-pipeline/security/sonarqube.yml", "yaml"
        )
        == "ops"
    )


def test_classify_ops_helm_templates_yaml():
    assert (
        _classify_file_symbol_type(
            "templates/service/templates/deployment.yaml", "yaml"
        )
        == "ops"
    )


def test_classify_java_under_templates_not_ops():
    assert (
        _classify_file_symbol_type(
            "src/main/java/com/example/templates/Service.java", "java"
        )
        is None
    )


def test_classify_kotlin_under_build_pipeline_not_ops():
    assert (
        _classify_file_symbol_type(
            "build-pipeline/scripts/deploy.kts", "kotlin"
        )
        is None
    )


def test_classify_manifest_inside_build_pipeline():
    assert (
        _classify_file_symbol_type("build-pipeline/pom.xml", "xml")
        == "manifest"
    )


def test_classify_xml_under_templates_not_ops():
    assert (
        _classify_file_symbol_type(
            "src/main/resources/templates/report.xml", "xml"
        )
        is None
    )


JAVA_ABSTRACT_UDH = """\
package com.example;

public abstract class AbstractUdhBusinessService {
    @Autowired
    protected FeatureManagmentService featureManagmentService;
}
"""

JAVA_CREATE_TIE = """\
package com.example;

public class CreateTieBusinessService extends AbstractUdhBusinessService {
    public void createTie(String flag) {
        if (featureManagmentService.isEnabled(flag)) {
            doWork();
        }
    }
}
"""

JAVA_LOGIN = """\
package com.example;

public class LoginBusinessService extends AbstractUdhBusinessService {
    public void login() {
        doLogin();
    }
}
"""


def _chunk_by_symbol(source: str, rel_path: str, symbol_name: str):
    chunks = chunk_file(source, rel_path, "java", "deadbeef")
    return next(c for c in chunks if c.symbol_name == symbol_name)


def test_java_inheritance_callees_distinguish_caller_from_inheritor():
    """Call sites must not be attributed to passive field holders or inheritors."""
    abstract_chunk = _chunk_by_symbol(
        JAVA_ABSTRACT_UDH, "AbstractUdhBusinessService.java", "AbstractUdhBusinessService"
    )
    create_tie_chunk = _chunk_by_symbol(
        JAVA_CREATE_TIE, "CreateTieBusinessService.java", "CreateTieBusinessService"
    )
    login_chunk = _chunk_by_symbol(
        JAVA_LOGIN, "LoginBusinessService.java", "LoginBusinessService"
    )

    assert "isEnabled" in create_tie_chunk.callees
    assert "featureManagmentService.isEnabled" in create_tie_chunk.callees

    assert "isEnabled" not in login_chunk.callees
    assert "featureManagmentService.isEnabled" not in login_chunk.callees

    assert "isEnabled" not in abstract_chunk.callees
    assert "featureManagmentService.isEnabled" not in abstract_chunk.callees


def test_import_header_does_not_inject_false_callees():
    """Prepended imports must not add call tokens to declaration-only chunks."""
    source = """\
package com.example;

import com.udh.feature.FeatureManagmentService;

public class PassiveHolder {
    private FeatureManagmentService featureManagmentService;
}
"""
    chunk = _chunk_by_symbol(source, "PassiveHolder.java", "PassiveHolder")

    assert "import com.udh.feature.FeatureManagmentService" in chunk.content
    assert "isEnabled" not in chunk.callees
    assert "featureManagmentService.isEnabled" not in chunk.callees
    assert "FeatureManagmentService" not in chunk.callees
