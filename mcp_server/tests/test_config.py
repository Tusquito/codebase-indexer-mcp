"""Unit tests for Settings parsing helpers."""

from codebase_indexer.config import Settings


def test_service_url_keyword_list_parses_csv():
    s = Settings(service_url_keywords="a, b ,c,")
    assert s.service_url_keyword_list == ["a", "b", "c"]


def test_service_discovery_extra_query_list_handles_pipe_and_newlines():
    s = Settings(service_discovery_extra_queries="q1|q2\n q3 ")
    assert s.service_discovery_extra_query_list == ["q1", "q2", "q3"]


def test_service_discovery_extra_query_list_empty_by_default():
    assert Settings().service_discovery_extra_query_list == []


def test_auth_token_defaults_empty():
    assert Settings().mcp_auth_token == ""
