"""Unit tests for cross-reference path matching and classification."""

from codebase_indexer.tools.cross_references import (
    _build_url_extractors,
    _classify_reference,
    _extract_code_urls,
    _extract_route_paths,
    _paths_match,
    configure_url_keywords,
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
