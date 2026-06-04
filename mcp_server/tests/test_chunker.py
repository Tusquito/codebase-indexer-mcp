"""Unit tests for the tree-sitter / sliding-window chunker."""

from codebase_indexer.indexer.chunker import chunk_file


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


def test_import_header_prepended_to_python_chunks():
    """Import statements should appear in every function/class chunk."""
    source = '''\
import os
from typing import List

def greet(name: str) -> str:
    return f"Hello {name}"


class Greeter:
    def say_hello(self) -> None:
        print("hi")
'''
    chunks = chunk_file(source, "greet.py", "python", "abc")
    # Every chunk should carry the import context
    for chunk in chunks:
        assert "import os" in chunk.content, f"Missing import in chunk: {chunk.symbol_name}"
        assert "from typing import List" in chunk.content


def test_import_header_prepended_to_java_chunks():
    """Java import declarations should appear in every class/method chunk."""
    source = '''\
package com.example;

import com.udh.interface.UserService;
import java.util.List;

public class UserController {
    private UserService userService;

    public List<String> getUsers() {
        return userService.findAll();
    }
}
'''
    chunks = chunk_file(source, "UserController.java", "java", "abc")
    for chunk in chunks:
        assert "import com.udh.interface.UserService" in chunk.content, (
            f"Missing import in chunk: {chunk.symbol_name}"
        )


def test_import_header_prepended_to_csharp_chunks():
    """C# using directives should appear in class chunks."""
    source = '''\
using System;
using MyCompany.Core.Interfaces;

namespace MyApp
{
    public class Service
    {
        public void Run() { }
    }
}
'''
    chunks = chunk_file(source, "Service.cs", "csharp", "abc")
    for chunk in chunks:
        assert "using MyCompany.Core.Interfaces" in chunk.content, (
            f"Missing using directive in chunk: {chunk.symbol_name}"
        )


def test_no_import_duplication_when_chunk_starts_with_imports():
    """Import header should not be prepended to chunks that already start with it."""
    source = '''\
import os


def foo():
    return os.getcwd()
'''
    chunks = chunk_file(source, "foo.py", "python", "abc")
    for chunk in chunks:
        # Count occurrences of "import os" — should not be duplicated
        assert chunk.content.count("import os") == 1, (
            f"Duplicate import in chunk: {chunk.symbol_name}"
        )
