"""AST-aware chunking tests for SQL (tree-sitter-sql)."""

import pytest

from codebase_indexer.indexer.chunker import (
    _classify_symbol_type,
    chunk_file,
)

SQL_HASH = "deadbeef"


def _chunk(sql: str, rel_path: str = "schema.sql") -> list:
    return chunk_file(sql, rel_path, "sql", SQL_HASH)


def _one(sql: str):
    chunks = _chunk(sql)
    assert len(chunks) == 1, [(c.symbol_name, c.symbol_type) for c in chunks]
    return chunks[0]


def test_create_table_extracts_qualified_name():
    chunk = _one(
        """CREATE TABLE dbo.users (
    id INT PRIMARY KEY,
    name NVARCHAR(100) NOT NULL
);"""
    )
    assert chunk.symbol_name == "dbo.users"
    assert chunk.symbol_type == "table"
    assert chunk.language == "sql"
    assert chunk.file_sha256 == SQL_HASH
    assert "CREATE TABLE" in chunk.content


def test_create_function_tsql_style():
    chunk = _one(
        """CREATE FUNCTION dbo.AddOne(@x int)
RETURNS int
AS
BEGIN
  RETURN @x + 1
END;"""
    )
    assert chunk.symbol_name == "dbo.AddOne"
    assert chunk.symbol_type == "function"


def test_create_view_extracts_name():
    chunk = _one(
        """CREATE VIEW dbo.ActiveUsers AS
SELECT id, name FROM dbo.users WHERE active = 1;"""
    )
    assert chunk.symbol_name == "dbo.ActiveUsers"
    assert chunk.symbol_type == "view"


def test_create_type_table_valued_parameter():
    chunk = _one(
        """CREATE TYPE dbo.UserIdList AS TABLE (
    user_id INT NOT NULL,
    PRIMARY KEY (user_id)
);"""
    )
    assert chunk.symbol_name == "dbo.UserIdList"
    assert chunk.symbol_type == "type"


def test_create_index_extracts_name():
    chunk = _one("CREATE INDEX idx_users_name ON dbo.users (name);")
    assert chunk.symbol_name == "idx_users_name"
    assert chunk.symbol_type == "index"


def test_create_trigger_postgresql_style():
    chunk = _one(
        """CREATE TRIGGER update_timestamp
BEFORE UPDATE ON users
FOR EACH ROW
EXECUTE FUNCTION update_modified_column();"""
    )
    assert chunk.symbol_name == "update_timestamp"
    assert chunk.symbol_type == "trigger"


@pytest.mark.xfail(
    strict=True,
    reason="tree-sitter-sql 0.3.11 has no create_procedure node kind in the grammar",
)
def test_create_procedure_tsql_style():
    chunk = _one(
        """CREATE PROCEDURE dbo.GetUsers(@status int)
AS
BEGIN
  SELECT * FROM users WHERE status = @status
END;"""
    )
    assert chunk.symbol_name == "dbo.GetUsers"
    assert chunk.symbol_type == "procedure"


def test_classify_sql_symbol_types():
    assert _classify_symbol_type("create_table") == "table"
    assert _classify_symbol_type("create_procedure") == "procedure"
    assert _classify_symbol_type("create_function") == "function"
    assert _classify_symbol_type("create_view") == "view"
    assert _classify_symbol_type("create_trigger") == "trigger"
    assert _classify_symbol_type("create_type") == "type"
    assert _classify_symbol_type("create_index") == "index"


def test_mixed_sql_file_one_chunk_per_object():
    sql = """CREATE TABLE dbo.users (id INT);

CREATE PROCEDURE dbo.GetUsers(@status int)
AS
BEGIN
  SELECT 1
END;

CREATE FUNCTION dbo.AddOne(@x int)
RETURNS int
AS
BEGIN
  RETURN @x
END;

CREATE VIEW dbo.ActiveUsers AS SELECT id FROM dbo.users;

CREATE TYPE dbo.UserIdList AS TABLE (user_id INT);
"""
    chunks = _chunk(sql)
    by_type = {(c.symbol_name, c.symbol_type) for c in chunks}
    assert ("dbo.users", "table") in by_type
    assert ("dbo.AddOne", "function") in by_type
    assert ("dbo.ActiveUsers", "view") in by_type
    assert ("dbo.UserIdList", "type") in by_type
    assert len(chunks) >= 4


def test_large_sql_table_splits_with_inherited_metadata():
    """Large CREATE TABLE hits _split_large_node; sub-chunks keep parent symbol metadata."""
    cols = ",\n".join(f"    col_{i} INT" for i in range(80))
    sql = f"CREATE TABLE dbo.BigTable (\n{cols}\n);"
    chunks = _chunk(sql)
    assert len(chunks) >= 2
    inherited = [
        c for c in chunks
        if c.symbol_name == "dbo.BigTable" and c.symbol_type == "table"
    ]
    assert len(inherited) >= 2
    covered_lines = set()
    for c in chunks:
        covered_lines.update(range(c.start_line, c.end_line + 1))
    assert min(covered_lines) == 1
    assert max(covered_lines) >= 80


def test_sql_chunk_ids_are_deterministic():
    sql = "CREATE TABLE users (id INT);"
    a = _chunk(sql, "a.sql")
    b = _chunk(sql, "a.sql")
    assert [c.chunk_id for c in a] == [c.chunk_id for c in b]
