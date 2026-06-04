"""Unit tests for the tree-sitter / sliding-window chunker."""

from codebase_indexer.indexer.chunker import (
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
