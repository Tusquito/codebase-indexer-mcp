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
