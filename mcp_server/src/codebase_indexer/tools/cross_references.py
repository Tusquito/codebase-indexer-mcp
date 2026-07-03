# src/codebase_indexer/tools/cross_references.py
"""MCP tool: find_cross_references — discover links across collections."""

from __future__ import annotations

import re
from collections import defaultdict
from typing import TYPE_CHECKING

from fastmcp import FastMCP

from codebase_indexer.config import DEFAULT_SERVICE_URL_KEYWORDS
from codebase_indexer.tools.build_deps import extract_build_deps, is_build_manifest
from codebase_indexer.tools.search_common import run_search

if TYPE_CHECKING:
    from codebase_indexer.context import AppContext


# ---------------------------------------------------------------------------
# URL / route path extraction
# ---------------------------------------------------------------------------

# Extract route paths from endpoint definitions.
# Capture groups use a bounded {1,300} repetition rather than open-ended `+`:
# these patterns run over arbitrary indexed file content, and an upper bound
# caps the work the backtracking engine can do on adversarial input (route
# literals are never legitimately that long).
_ROUTE_EXTRACTORS = [
    # Java Spring: @RequestMapping("/rest/login/"), @GetMapping("/foo")
    re.compile(r'@(?:Request|Get|Post|Put|Delete|Patch)Mapping\s*\(\s*(?:value\s*=\s*|path\s*=\s*)?["\']([^"\']{1,300})["\']', re.IGNORECASE),
    # C# ASP.NET: [Route("me/email")], [HttpGet("profile")]
    re.compile(r'\[(?:Route|Http(?:Get|Post|Put|Delete|Patch))\s*\(\s*["\']([^"\']{1,300})["\']', re.IGNORECASE),
    # C# minimal API: .MapGet("/api/foo", ...)
    re.compile(r'\.Map(?:Get|Post|Put|Delete|Patch)\s*\(\s*["\']([^"\']{1,300})["\']', re.IGNORECASE),
    # Node/Express: app.get("/foo", ...), router.post("/bar", ...)
    re.compile(r'(?:app|router)\.(?:get|post|put|delete|patch)\s*\(\s*["\']([^"\']{1,300})["\']', re.IGNORECASE),
]

# Default URL path keywords (project-agnostic), sourced from the single
# definition in config.py. Override per-codebase via the SERVICE_URL_KEYWORDS
# env var (see Settings.service_url_keywords) — no source edits required.
_DEFAULT_URL_KEYWORDS = [
    k.strip() for k in DEFAULT_SERVICE_URL_KEYWORDS.split(",") if k.strip()
]


def _build_url_extractors(
    keywords: list[str],
) -> tuple[list[re.Pattern], list[re.Pattern]]:
    """Compile the keyword-driven URL extractors from a keyword list.

    Version segments like /v1/ /v2/ are always recognised regardless of the
    configured keywords.
    """
    kw = "|".join([re.escape(k) for k in keywords] + [r"v\d+"])
    # All capture groups use bounded repetitions ({0,N}/{1,N}) rather than
    # open-ended `+`/`*`. These extractors scan arbitrary indexed file content,
    # so bounding the match length caps backtracking work on adversarial input
    # while comfortably covering any realistic URL/path (≤ a few hundred chars).
    config_extractors = [
        # YAML/properties: key: /api/login or key: /rest/login/
        re.compile(rf':\s*(/(?:{kw})/[^\s,}}\]"\']{{1,200}})', re.IGNORECASE),
        # Broader: any value starting with / followed by >=2 path segments
        re.compile(r':\s*(/[a-zA-Z][a-zA-Z0-9_-]{0,100}/[a-zA-Z][a-zA-Z0-9_{}/-]{0,200})', re.MULTILINE),
        # host/baseUrl config: host: http://...
        re.compile(r'(?:host|baseUrl|baseAddress|base-url|base_url|base\.url|endpoint|uri)\s*[:=]\s*["\']?(https?://[^\s"\']{1,300})', re.IGNORECASE),
    ]
    code_extractors = [
        # String literals with path-like values: "/api/login", "/rest/login/"
        re.compile(rf'["\'](/(?:{kw})/[^"\']{{1,200}})["\']'),
        # RestTemplate / WebClient URL args with path segments
        re.compile(r'(?:exchange|getForObject|getForEntity|postForObject|postForEntity|put|delete|retrieve|uri)\s*\(\s*["\']([^"\']{0,200}?/[a-zA-Z][a-zA-Z0-9_/-]{1,200})["\']', re.IGNORECASE),
    ]
    return config_extractors, code_extractors


def _route_paths_impl(content: str, rel_path: str = "") -> list[str]:
    """Extract API route paths from endpoint definition code.

    Handles ASP.NET [controller] token by substituting the controller name
    derived from the file path (e.g., EmailController.cs → email).
    """
    paths = []
    for pattern in _ROUTE_EXTRACTORS:
        for m in pattern.finditer(content):
            path = m.group(1).strip("/")
            if path and len(path) > 1:
                # Resolve ASP.NET [controller] token
                if "[controller]" in path.lower() and rel_path:
                    import os
                    fname = os.path.basename(rel_path).replace(".cs", "")
                    ctrl_name = fname.replace("Controller", "").lower()
                    if ctrl_name:
                        resolved = re.sub(r'\[controller\]', ctrl_name, path, flags=re.IGNORECASE)
                        paths.append(resolved)
                else:
                    paths.append(path)
    return paths


class UrlExtractors:
    """Keyword-driven URL/route extraction + reference classification.

    One instance is built per app from the configured SERVICE_URL_KEYWORDS and
    injected into the tools (see AppContext), replacing the previous module-global
    extractor state that was mutated at registration time.
    """

    def __init__(self, keywords: list[str] | None = None) -> None:
        """Build extractors from keyword list; defaults to SERVICE_URL_KEYWORDS."""
        self.reconfigure(keywords or _DEFAULT_URL_KEYWORDS)

    def reconfigure(self, keywords: list[str]) -> None:
        """Recompile config and code URL regexes from a new keyword list."""
        self._config_extractors, self._code_extractors = _build_url_extractors(
            keywords or _DEFAULT_URL_KEYWORDS
        )

    @staticmethod
    def route_paths(content: str, rel_path: str = "") -> list[str]:
        """Extract API route paths from controller/handler definition code."""
        return _route_paths_impl(content, rel_path)

    def config_urls(self, content: str) -> tuple[list[str], list[str]]:
        """Extract (paths, base_urls) from config content."""
        paths: list[str] = []
        base_urls: list[str] = []
        for pattern in self._config_extractors:
            for m in pattern.finditer(content):
                val = m.group(1)
                if val.startswith("http"):
                    base_urls.append(val)
                else:
                    path = val.strip("/")
                    if path and len(path) > 2:
                        paths.append(path)
        return paths, base_urls

    def code_urls(self, content: str) -> list[str]:
        """Extract URL paths from code (string literals)."""
        paths: list[str] = []
        for pattern in self._code_extractors:
            for m in pattern.finditer(content):
                path = m.group(1).strip("/")
                if path and len(path) > 2:
                    paths.append(path)
        return paths

    def classify_reference(self, content: str, symbol_or_query: str, rel_path: str = "") -> str:
        """Classify chunk content as definition, import, http_call, build_dependency, etc."""
        return _classify_reference_impl(self, content, symbol_or_query, rel_path)


# Module-level default instance + thin back-compat free functions. Production
# code uses an injected UrlExtractors (via AppContext); these remain for tests
# and standalone callers, and avoid the previous mutate-global pattern.
_DEFAULT_EXTRACTORS = UrlExtractors()


def configure_url_keywords(keywords: list[str]) -> None:
    """Reconfigure the module default extractor (idempotent, no-op if empty)."""
    if keywords:
        _DEFAULT_EXTRACTORS.reconfigure(keywords)


def _extract_route_paths(content: str, rel_path: str = "") -> list[str]:
    """Back-compat wrapper for route path extraction."""
    return _route_paths_impl(content, rel_path)


def _extract_config_urls(content: str) -> tuple[list[str], list[str]]:
    """Back-compat wrapper using the module default UrlExtractors instance."""
    return _DEFAULT_EXTRACTORS.config_urls(content)


def _extract_code_urls(content: str) -> list[str]:
    """Back-compat wrapper for code URL literal extraction."""
    return _DEFAULT_EXTRACTORS.code_urls(content)


def _paths_match(caller_path: str, endpoint_path: str) -> bool:
    """Check if a caller path references an endpoint path.

    Handles partial matches: caller="/profile/me/email/{id}" matches
    endpoint="me/email", and caller="profile/me/core" matches endpoint="me/core".

    Guards against false positives:
    - Both paths must have at least 2 segments (e.g. "rest/cache", not just "app")
    - Match must be on full path segments, not arbitrary substrings
    """
    def norm(p: str) -> str:
        p = re.sub(r'\{[^}]+\}', '', p).strip('/')
        return p.lower()

    cn = norm(caller_path)
    en = norm(endpoint_path)
    if not cn or not en:
        return False

    cn_segs = [s for s in cn.split('/') if s]
    en_segs = [s for s in en.split('/') if s]

    # Require at least 2 segments on both sides to avoid false positives
    if len(cn_segs) < 2 or len(en_segs) < 2:
        return False

    # Check if the shorter segment list appears as a contiguous subsequence
    # of the longer one (segment-aligned, not substring)
    shorter, longer = (en_segs, cn_segs) if len(en_segs) <= len(cn_segs) else (cn_segs, en_segs)
    for i in range(len(longer) - len(shorter) + 1):
        if longer[i:i + len(shorter)] == shorter:
            return True
    return False


# ---------------------------------------------------------------------------
# Reference classification (improved)
# ---------------------------------------------------------------------------

_IMPORT_PATTERNS = re.compile(
    r"^(?:import\s|from\s|require\(|using\s|#include)", re.MULTILINE
)
_ENDPOINT_DEF_PATTERNS = re.compile(
    r"@(?:Get|Post|Put|Delete|Patch|Request)Mapping|"
    r"@(?:GET|POST|PUT|DELETE|PATCH|Path)\b|"
    r"\[(?:Http(?:Get|Post|Put|Delete|Patch)|Route|ApiController)\]|"
    r"\.Map(?:Get|Post|Put|Delete|Patch)\(|"
    r"app\.(?:get|post|put|delete|patch)\(|"
    r"router\.(?:get|post|put|delete)\(",
    re.IGNORECASE,
)
_HTTP_CALL_PATTERNS = re.compile(
    r"RestTemplate|WebClient\.(?:create|builder)|\.exchange\(|\.retrieve\(|"
    r"@FeignClient|"
    r"HttpClient|IHttpClientFactory|\.GetAsync\(|\.PostAsync\(|\.PutAsync\(|"
    r"\.DeleteAsync\(|\.SendAsync\(|\.GetFromJsonAsync\(|\.PostAsJsonAsync\(|"
    r"httpx\.|requests\.|fetch\(|axios\.|"
    r"\.get\(\s*[\"']https?://|\.post\(\s*[\"']https?://",
    re.IGNORECASE,
)
_CONFIG_FILE_PATTERNS = re.compile(
    r"\.(ya?ml|json|properties|env|config)$", re.IGNORECASE
)
_CLASS_DEF_PATTERNS = re.compile(
    r"^\s*(?:public\s+|private\s+|protected\s+|internal\s+)?(?:abstract\s+|static\s+|sealed\s+|partial\s+)*"
    r"(?:class|interface|enum|record|struct)\s+",
    re.MULTILINE,
)


def _classify_reference_impl(
    extractors: "UrlExtractors", content: str, symbol_or_query: str, rel_path: str = ""
) -> str:
    """Classify the type of cross-reference based on chunk content."""
    # Build manifest files with dependency declarations → build_dependency
    if is_build_manifest(rel_path):
        deps = extract_build_deps(content, rel_path)
        if deps:
            return "build_dependency"

    # Config files with URL paths → service_config
    if _CONFIG_FILE_PATTERNS.search(rel_path):
        paths, base_urls = extractors.config_urls(content)
        if base_urls or paths:
            return "service_config"

    if _CLASS_DEF_PATTERNS.search(content):
        if symbol_or_query and re.search(
            rf"(?:class|interface|enum|record)\s+{re.escape(symbol_or_query)}\b",
            content,
        ):
            return "definition"
    if _ENDPOINT_DEF_PATTERNS.search(content):
        return "endpoint_definition"
    if _HTTP_CALL_PATTERNS.search(content):
        return "http_call"
    if _IMPORT_PATTERNS.search(content):
        return "import"
    return "usage"


def _classify_reference(content: str, symbol_or_query: str, rel_path: str = "") -> str:
    """Back-compat wrapper classifying via the module default extractor."""
    return _classify_reference_impl(_DEFAULT_EXTRACTORS, content, symbol_or_query, rel_path)


# ---------------------------------------------------------------------------
# Tool registration
# ---------------------------------------------------------------------------

def register_cross_references_tool(mcp: FastMCP, ctx: "AppContext") -> None:
    """Register the find_cross_references MCP tool on the FastMCP instance."""
    storage = ctx.storage
    embedder = ctx.embedder
    extractors = ctx.url_extractors

    @mcp.tool(
        name="find_cross_references",
        description=(
            "Find cross-project links for a symbol, endpoint, concept, or any query "
            "across multiple indexed collections. Discovers: direct code links "
            "(imports, class usage, call sites), HTTP endpoint connections "
            "(controller in one project, client call in another), shared "
            "DTOs/error codes, and semantic relationships. "
            "Each result is classified as: definition, import, usage, "
            "endpoint_definition, http_call, or call_site. "
            "Use 'query' for semantic search (best for endpoints, concepts, "
            "partial names) or 'symbol_name' for exact symbol matching. "
            "For precise method call-site retrieval, pass 'member' (method name, "
            "e.g. isEnabled) and optionally 'receiver' (field/var name, e.g. "
            "featureManagmentService) to disambiguate; this uses indexed callees "
            "rather than semantic search. Consumer accuracy for inherited fields "
            "requires 'member' (call-expression grounding) rather than "
            "semantic/import search. "
            "When RERANK_ENABLED=true, pass rerank=false to skip ColBERT "
            "query embed and MAX_SIM on semantic search paths (hybrid RRF only)."
        ),
    )
    async def find_cross_references(
        query: str | None = None,
        symbol_name: str | None = None,
        collections: list[str] | None = None,
        top_k: int = 10,
        member: str | None = None,
        receiver: str | None = None,
        rerank: bool | None = None,
    ) -> dict:
        if not query and not symbol_name and not member:
            return {"error": "Provide at least 'query', 'symbol_name', or 'member'."}

        if collections:
            target_collections = collections
        else:
            stats = await storage.list_collection_stats()
            target_collections = [s.name for s in stats]

        if not target_collections:
            return {"query": query, "symbol_name": symbol_name, "found_in": {}, "collection_count": 0}

        all_results: list[dict] = []
        lookup_label = query or symbol_name or ""

        # Semantic search
        if query:
            semantic_results = await run_search(
                storage,
                embedder,
                query,
                target_collections,
                top_k,
                language=None,
                min_score=0.3,
                rerank=rerank,
            )
            for r in semantic_results:
                all_results.append({
                    "rel_path": r.rel_path,
                    "symbol_name": r.symbol_name,
                    "symbol_type": r.symbol_type,
                    "start_line": r.start_line,
                    "end_line": r.end_line,
                    "language": r.language,
                    "content": r.content,
                    "score": round(r.score, 4),
                    "collection": r.collection,
                    "match_type": "semantic",
                    "reference_type": extractors.classify_reference(r.content, lookup_label, r.rel_path),
                })

        # Exact symbol match
        if symbol_name:
            symbol_results = await storage.find_symbol_in_collections(
                symbol_name=symbol_name,
                collections=target_collections,
                limit_per_collection=top_k,
            )
            seen_chunks = {r["rel_path"] + str(r["start_line"]) for r in all_results}
            for r in symbol_results:
                key = r.rel_path + str(r.start_line)
                if key not in seen_chunks:
                    seen_chunks.add(key)
                    all_results.append({
                        "rel_path": r.rel_path,
                        "symbol_name": r.symbol_name,
                        "symbol_type": r.symbol_type,
                        "start_line": r.start_line,
                        "end_line": r.end_line,
                        "language": r.language,
                        "content": r.content,
                        "score": 1.0,
                        "collection": r.collection,
                        "match_type": "exact_symbol",
                        "reference_type": extractors.classify_reference(r.content, symbol_name, r.rel_path),
                    })

            # Also run an import-phrased semantic search to surface chunks from
            # other projects that *import* the symbol but aren't named after it.
            # With import headers now prepended to every AST chunk, this search
            # reliably finds consumer files that reference the library type.
            import_query = f"import {symbol_name}"
            import_results = await run_search(
                storage,
                embedder,
                import_query,
                target_collections,
                top_k,
                language=None,
                min_score=0.3,
                rerank=rerank,
            )
            seen_chunks = {r["rel_path"] + str(r["start_line"]) for r in all_results}
            for r in import_results:
                key = r.rel_path + str(r.start_line)
                if key not in seen_chunks:
                    seen_chunks.add(key)
                    all_results.append({
                        "rel_path": r.rel_path,
                        "symbol_name": r.symbol_name,
                        "symbol_type": r.symbol_type,
                        "start_line": r.start_line,
                        "end_line": r.end_line,
                        "language": r.language,
                        "content": r.content,
                        "score": round(r.score, 4),
                        "collection": r.collection,
                        "match_type": "import_search",
                        "reference_type": extractors.classify_reference(r.content, symbol_name, r.rel_path),
                    })

        # Path D: precise call-site retrieval via indexed callees
        if member:
            caller_results = await storage.find_callers_in_collections(
                method=member,
                collections=target_collections,
                receiver=receiver,
                limit_per_collection=top_k,
            )
            result_by_key = {
                r["rel_path"] + str(r["start_line"]): r for r in all_results
            }
            for r in caller_results:
                key = r.rel_path + str(r.start_line)
                existing = result_by_key.get(key)
                if existing is not None:
                    existing["match_type"] = "call_site"
                    existing["reference_type"] = "call_site"
                    existing["score"] = 1.0
                else:
                    entry = {
                        "rel_path": r.rel_path,
                        "symbol_name": r.symbol_name,
                        "symbol_type": r.symbol_type,
                        "start_line": r.start_line,
                        "end_line": r.end_line,
                        "language": r.language,
                        "content": r.content,
                        "score": 1.0,
                        "collection": r.collection,
                        "match_type": "call_site",
                        "reference_type": "call_site",
                    }
                    all_results.append(entry)
                    result_by_key[key] = entry

        # Group by collection
        by_collection: dict[str, list[dict]] = defaultdict(list)
        for result in all_results:
            coll = result.pop("collection")
            by_collection[coll].append(result)

        link_summary = _build_link_summary(
            by_collection, extractors, member=member, symbol_name=symbol_name
        )

        return {
            "query": query,
            "symbol_name": symbol_name,
            "member": member,
            "receiver": receiver,
            "collection_count": len(by_collection),
            "found_in": dict(by_collection),
            "links": link_summary,
        }


def _build_link_summary(
    by_collection: dict[str, list[dict]],
    extractors: "UrlExtractors",
    member: str | None = None,
    symbol_name: str | None = None,
) -> list[dict]:
    """Build a summary of cross-collection links using URL path matching.

    Instead of creating cartesian product links between all http_calls and
    all endpoint_definitions, extracts actual URL paths from both sides and
    only creates links where paths actually match.
    """
    links = []

    # Collect endpoints with their extracted route paths
    endpoints: list[tuple[str, dict, list[str]]] = []  # (collection, result, paths)
    # Collect callers: http_calls + service_configs with their URL paths
    callers: list[tuple[str, dict, list[str]]] = []
    # Collect definitions, usages, and precise call sites
    definitions: list[tuple[str, dict]] = []
    usages: list[tuple[str, dict]] = []
    call_sites: list[tuple[str, dict]] = []

    for coll, results in by_collection.items():
        for r in results:
            ref = r.get("reference_type", "usage")
            content = r.get("content", "")

            if ref == "endpoint_definition":
                paths = extractors.route_paths(content, r.get("rel_path", ""))
                endpoints.append((coll, r, paths))
            elif ref == "http_call":
                paths = extractors.code_urls(content)
                callers.append((coll, r, paths))
            elif ref == "service_config":
                paths, _ = extractors.config_urls(content)
                callers.append((coll, r, paths))
            elif ref == "definition":
                definitions.append((coll, r))
            elif ref == "call_site":
                call_sites.append((coll, r))
            elif ref in ("usage", "import"):
                usages.append((coll, r))

    # definition ↔ call_site links (same-collection allowed; callees filter proves the call)
    seen_links: set[tuple] = set()
    for def_coll, def_r in definitions:
        def_symbol = def_r.get("symbol_name") or ""
        for use_coll, use_r in call_sites:
            if member:
                def_match = symbol_name
                if def_match and def_symbol and def_symbol != def_match:
                    continue
            elif symbol_name and def_symbol and def_symbol != symbol_name:
                continue
            link_key = (use_coll, use_r["rel_path"], def_coll, def_r["rel_path"])
            if link_key not in seen_links:
                seen_links.add(link_key)
                links.append({
                    "type": "code_dependency",
                    "from": {
                        "collection": use_coll,
                        "path": use_r["rel_path"],
                        "reference_type": "call_site",
                    },
                    "to": {
                        "collection": def_coll,
                        "path": def_r["rel_path"],
                        "symbol": def_symbol,
                    },
                })

    if len(by_collection) < 2:
        return links

    # Path-based endpoint matching (no more cartesian product)
    for call_coll, call_r, call_paths in callers:
        for ep_coll, ep_r, ep_paths in endpoints:
            if call_coll == ep_coll:
                continue
            # Check if any caller path matches any endpoint path
            matched_paths = []
            for cp in call_paths:
                for ep in ep_paths:
                    if _paths_match(cp, ep):
                        matched_paths.append(f"{cp} → {ep}")

            if matched_paths:
                link_key = (call_coll, call_r["rel_path"], ep_coll, ep_r["rel_path"])
                if link_key not in seen_links:
                    seen_links.add(link_key)
                    links.append({
                        "type": "http_dependency",
                        "from": {
                            "collection": call_coll,
                            "path": call_r["rel_path"],
                            "reference_type": call_r.get("reference_type"),
                        },
                        "to": {
                            "collection": ep_coll,
                            "path": ep_r["rel_path"],
                            "reference_type": "endpoint_definition",
                        },
                        "matched_paths": matched_paths,
                    })

    # definition ↔ usage/import links
    # Only create a link when the usage/import chunk actually mentions the
    # definition's symbol name — prevents noisy cartesian-product false positives.
    for def_coll, def_r in definitions:
        def_symbol = def_r.get("symbol_name") or ""
        for use_coll, use_r in usages:
            if def_coll == use_coll:
                continue
            # Require the usage chunk to contain the definition symbol name
            if def_symbol and def_symbol not in use_r.get("content", ""):
                continue
            link_key = (use_coll, use_r["rel_path"], def_coll, def_r["rel_path"])
            if link_key not in seen_links:
                seen_links.add(link_key)
                links.append({
                    "type": "code_dependency",
                    "from": {
                        "collection": use_coll,
                        "path": use_r["rel_path"],
                        "reference_type": use_r.get("reference_type"),
                    },
                    "to": {
                        "collection": def_coll,
                        "path": def_r["rel_path"],
                        "symbol": def_symbol,
                    },
                })

    return links
