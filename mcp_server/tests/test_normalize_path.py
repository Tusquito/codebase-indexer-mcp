"""Unit tests for path normalization and collection-name derivation."""

import pytest

from codebase_indexer.tools.index import _normalize_path, _derive_collection_name


@pytest.mark.parametrize(
    "raw, expected",
    [
        (r"C:\Users\me\Documents\Repositories\myproject", "/myproject"),
        ("C:/Users/me/Documents/Repositories/myproject", "/myproject"),
        ("/workspace/myproject", "/myproject"),
        ("/myproject", "/myproject"),
        ("myproject", "/myproject"),
        ("/", "/"),
        ("", "/"),
        ("  /workspace/nested/deep/proj  ", "/proj"),
        # Traversal attempts collapse to their last segment.
        ("../../etc/passwd", "/passwd"),
    ],
)
def test_normalize_path(raw, expected):
    assert _normalize_path(raw) == expected


def test_derive_collection_name_from_subpath():
    assert _derive_collection_name("/workspace", "/myproject") == "myproject"
    assert _derive_collection_name("/workspace", "myproject/sub") == "myproject"


def test_derive_collection_name_falls_back_to_workspace_basename():
    assert _derive_collection_name("/workspace/repos", "/") == "repos"
