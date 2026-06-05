# src/codebase_indexer/tools/service_map.py
"""MCP tool: map_service_dependencies — build E2E call chain across services."""

from __future__ import annotations

from collections import defaultdict
from typing import TYPE_CHECKING, Any

from fastmcp import FastMCP

from codebase_indexer.tools.build_deps import (
    extract_build_deps,
    is_build_manifest,
    match_deps_to_collections,
)
from codebase_indexer.tools.cross_references import _paths_match

if TYPE_CHECKING:
    from codebase_indexer.context import AppContext

# Generic, framework-oriented discovery queries (no project-specific terms).
# Extend per-codebase via the SERVICE_DISCOVERY_EXTRA_QUERIES env var instead of
# editing this list.
_DISCOVERY_QUERIES = [
    # Endpoint definitions
    "REST controller endpoint mapping route RequestMapping GetMapping PostMapping",
    "@RestController @RequestMapping @GetMapping @PostMapping @PutMapping @DeleteMapping",
    # HTTP client calls
    "RestTemplate exchange getForObject postForObject getForEntity",
    "WebClient create builder retrieve bodyToMono bodyToFlux",
    "HTTP client RestTemplate WebClient HttpClient base URL config",
    "@FeignClient Feign client interface",
    # Config / wiring
    "application.yml application.properties config host url endpoint",
    "base URL host address service connection configuration",
    "Feign client service connector proxy",
    # Generic integration shapes
    "adapter service operation request response",
    # Build / package manifests — surface inter-project compile dependencies
    "pom.xml dependency groupId artifactId version maven parent",
    "csproj PackageReference ProjectReference NuGet dotnet",
    "package.json dependencies devDependencies npm",
    "build.gradle implementation api dependency compile",
    "go.mod require module golang",
    "Cargo.toml dependencies crate rust",
    "pyproject.toml requirements.txt dependencies python",
]


def register_service_map_tool(mcp: FastMCP, ctx: "AppContext") -> None:
    """Register the map_service_dependencies MCP tool on the FastMCP instance."""
    settings = ctx.settings
    storage = ctx.storage
    embedder = ctx.embedder
    extractors = ctx.url_extractors

    @mcp.tool(
        name="map_service_dependencies",
        description=(
            "Automatically discover and map HTTP service dependencies across indexed "
            "collections. Scans all (or specified) collections for: endpoint definitions "
            "(controllers, route handlers), HTTP client calls (RestTemplate, HttpClient, "
            "Feign, WebClient), service configuration (base URLs, host settings in "
            "YAML/properties), and build-level dependencies (Maven pom.xml, NuGet .csproj, "
            "npm package.json, Gradle build files, go.mod, Cargo.toml, pyproject.toml). "
            "Returns a dependency graph showing which service calls which or depends on which, "
            "with matched endpoint paths and package references. Use this to understand "
            "microservice architecture and build E2E call chain maps."
        ),
    )
    async def map_service_dependencies(
        collections: list[str] | None = None,
        top_k: int = 30,
    ) -> dict:
        """Map service dependencies across collections.

        Args:
            collections: Collections to analyze. If omitted, analyzes all.
            top_k: Max results per query per collection (default 30).
        """
        if collections:
            target_collections = collections
        else:
            stats = await storage.list_collection_stats()
            target_collections = [s.name for s in stats]

        if len(target_collections) < 2:
            return {
                "error": "Need at least 2 indexed collections to map dependencies.",
                "collections_found": target_collections,
            }

        # Phase 1: Discover endpoints, clients, and configs in all collections.
        # Embed every discovery query in a single batched pass (one ONNX run +
        # one sparse embed run) instead of N sequential round-trips.
        queries = _DISCOVERY_QUERIES + settings.service_discovery_extra_query_list
        query_vectors = await embedder.embed_queries(queries)

        endpoints_by_coll: dict[str, list[dict]] = defaultdict(list)
        callers_by_coll: dict[str, list[dict]] = defaultdict(list)
        configs_by_coll: dict[str, list[dict]] = defaultdict(list)
        # Accumulates build manifest chunks; keyed by collection
        manifests_by_coll: dict[str, list[dict]] = defaultdict(list)

        seen_chunks: set[str] = set()

        for dense_vector, sparse_vector in query_vectors:
            results = await storage.search(
                collection=None,
                dense_vector=dense_vector,
                sparse_vector=sparse_vector,
                top_k=top_k,
                min_score=0.25,
                restrict_collections=target_collections,
            )

            for r in results:
                chunk_key = f"{r.collection}:{r.rel_path}:{r.start_line}"
                if chunk_key in seen_chunks:
                    continue
                seen_chunks.add(chunk_key)

                content = r.content
                entry: dict[str, Any] = {
                    "rel_path": r.rel_path,
                    "symbol_name": r.symbol_name,
                    "symbol_type": r.symbol_type,
                    "start_line": r.start_line,
                    "end_line": r.end_line,
                    "language": r.language,
                    "content_preview": content[:300],
                }

                # Classify what this chunk represents
                route_paths = extractors.route_paths(content, r.rel_path)
                if route_paths:
                    entry["routes"] = route_paths
                    endpoints_by_coll[r.collection].append(entry)
                    continue

                code_urls = extractors.code_urls(content)
                config_paths, base_urls = extractors.config_urls(content)

                if base_urls:
                    entry["base_urls"] = base_urls
                    entry["config_paths"] = config_paths
                    configs_by_coll[r.collection].append(entry)
                elif config_paths:
                    entry["config_paths"] = config_paths
                    configs_by_coll[r.collection].append(entry)
                elif code_urls:
                    entry["called_paths"] = code_urls
                    callers_by_coll[r.collection].append(entry)
                elif is_build_manifest(r.rel_path):
                    # Store the full content so Phase 2b can parse dependencies.
                    entry["content"] = content
                    manifests_by_coll[r.collection].append(entry)

        # Phase 2: Build dependency graph via path matching
        edges: list[dict] = []
        seen_edges: set[str] = set()

        # Match callers → endpoints (code HTTP calls)
        for caller_coll, caller_entries in callers_by_coll.items():
            for ep_coll, ep_entries in endpoints_by_coll.items():
                if caller_coll == ep_coll:
                    continue
                for caller in caller_entries:
                    for endpoint in ep_entries:
                        matched = set()
                        for cp in caller.get("called_paths", []):
                            for ep in endpoint.get("routes", []):
                                if _paths_match(cp, ep):
                                    matched.add(f"{cp} → {ep}")
                        if matched:
                            edge_key = f"{caller_coll}:{caller['rel_path']}→{ep_coll}:{endpoint['rel_path']}"
                            if edge_key not in seen_edges:
                                seen_edges.add(edge_key)
                                edges.append({
                                    "type": "http_call",
                                    "from_service": caller_coll,
                                    "from_file": caller["rel_path"],
                                    "from_symbol": caller.get("symbol_name"),
                                    "to_service": ep_coll,
                                    "to_file": endpoint["rel_path"],
                                    "to_symbol": endpoint.get("symbol_name"),
                                    "matched_routes": sorted(matched),
                                })

        # Match configs → endpoints (YAML/properties URL paths)
        for cfg_coll, cfg_entries in configs_by_coll.items():
            for ep_coll, ep_entries in endpoints_by_coll.items():
                if cfg_coll == ep_coll:
                    continue
                for cfg in cfg_entries:
                    for endpoint in ep_entries:
                        matched = set()
                        for cp in cfg.get("config_paths", []):
                            for ep in endpoint.get("routes", []):
                                if _paths_match(cp, ep):
                                    matched.add(f"{cp} → {ep}")
                        if matched:
                            edge_key = f"{cfg_coll}:{cfg['rel_path']}→{ep_coll}:{endpoint['rel_path']}"
                            if edge_key not in seen_edges:
                                seen_edges.add(edge_key)
                                edges.append({
                                    "type": "config_reference",
                                    "from_service": cfg_coll,
                                    "from_file": cfg["rel_path"],
                                    "to_service": ep_coll,
                                    "to_file": endpoint["rel_path"],
                                    "to_symbol": endpoint.get("symbol_name"),
                                    "matched_routes": sorted(matched),
                                    "base_urls": cfg.get("base_urls", []),
                                })

        # Phase 2b: Extract build dependencies from manifest chunks and match to collections
        build_dep_edges: list[dict] = []
        seen_build_edges: set[str] = set()

        for dep_coll, manifest_entries in manifests_by_coll.items():
            # Deduplicate by rel_path so we don't double-count multi-chunk manifests.
            # Merge content chunks for the same file before parsing.
            content_by_path: dict[str, str] = {}
            for entry in manifest_entries:
                path = entry["rel_path"]
                content_by_path[path] = content_by_path.get(path, "") + "\n" + entry.get("content", "")

            for rel_path, merged_content in content_by_path.items():
                deps = extract_build_deps(merged_content, rel_path)
                matches = match_deps_to_collections(
                    deps, target_collections, self_collection=dep_coll
                )
                for m in matches:
                    edge_key = f"{dep_coll}:{rel_path}→{m['matched_collection']}:{m['artifact']}"
                    if edge_key in seen_build_edges:
                        continue
                    seen_build_edges.add(edge_key)
                    build_dep_edges.append({
                        "type": "build_dependency",
                        "from_service": dep_coll,
                        "from_file": rel_path,
                        "to_service": m["matched_collection"],
                        "artifact": m["artifact"],
                        "group": m["group"],
                        "version": m["version"],
                        "scope": m["scope"],
                        "ecosystem": m["ecosystem"],
                        "match_confidence": m["match_confidence"],
                    })

        edges = [*edges, *build_dep_edges]
        adjacency: dict[str, list[str]] = defaultdict(list)
        for edge in edges:
            target = edge["to_service"]
            source = edge["from_service"]
            if target not in adjacency[source]:
                adjacency[source].append(target)

        # Build per-service summary
        services = {}
        for coll in target_collections:
            ep_count = len(endpoints_by_coll.get(coll, []))
            caller_count = len(callers_by_coll.get(coll, []))
            cfg_count = len(configs_by_coll.get(coll, []))
            manifest_count = len(manifests_by_coll.get(coll, []))
            build_dep_count = sum(
                1 for e in build_dep_edges if e["from_service"] == coll
            )
            services[coll] = {
                "endpoints_found": ep_count,
                "http_callers_found": caller_count,
                "configs_found": cfg_count,
                "build_manifests_found": manifest_count,
                "build_deps_found": build_dep_count,
                "calls": adjacency.get(coll, []),
                "called_by": [
                    src for src, targets in adjacency.items() if coll in targets
                ],
            }

        return {
            "collections_analyzed": target_collections,
            "services": services,
            "dependency_graph": dict(adjacency),
            "edges": edges,
            "summary": {
                "total_endpoints": sum(len(v) for v in endpoints_by_coll.values()),
                "total_callers": sum(len(v) for v in callers_by_coll.values()),
                "total_configs": sum(len(v) for v in configs_by_coll.values()),
                "total_build_deps": len(build_dep_edges),
                "total_edges": len(edges),
            },
        }
