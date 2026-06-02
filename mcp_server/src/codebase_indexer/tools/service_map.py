# src/codebase_indexer/tools/service_map.py
"""MCP tool: map_service_dependencies — build E2E call chain across services."""

import asyncio
from collections import defaultdict

from fastmcp import FastMCP

from codebase_indexer.config import Settings
from codebase_indexer.indexer.embedder import Embedder
from codebase_indexer.storage.qdrant import QdrantStorage
from codebase_indexer.tools.cross_references import (
    _extract_route_paths,
    _extract_config_urls,
    _extract_code_urls,
    _paths_match,
)

# Queries that discover endpoint definitions, HTTP clients, and config files
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
    # Domain-specific discovery (adapted to UDH codebase)
    "custisRest profile membership indicator api URL path",
    "adapter service operation request response",
]


def register_service_map_tool(
    mcp: FastMCP, settings: Settings, storage: QdrantStorage
) -> None:
    @mcp.tool(
        name="map_service_dependencies",
        description=(
            "Automatically discover and map HTTP service dependencies across indexed "
            "collections. Scans all (or specified) collections for: endpoint definitions "
            "(controllers, route handlers), HTTP client calls (RestTemplate, HttpClient, "
            "Feign, WebClient), and service configuration (base URLs, host settings in "
            "YAML/properties). Returns a dependency graph showing which service calls "
            "which, with matched endpoint paths. Use this to understand microservice "
            "architecture and build E2E call chain maps."
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

        embedder = Embedder(
            model=settings.embed_model,
            vector_size=settings.vector_size,
            hybrid=settings.hybrid_search,
        )

        # Phase 1: Discover endpoints, clients, and configs in all collections
        endpoints_by_coll: dict[str, list[dict]] = defaultdict(list)
        callers_by_coll: dict[str, list[dict]] = defaultdict(list)
        configs_by_coll: dict[str, list[dict]] = defaultdict(list)

        seen_chunks: set[str] = set()

        for query_text in _DISCOVERY_QUERIES:
            dense_vector = (await embedder.embed_batch_dense([query_text]))[0]

            sparse_vector = None
            if settings.hybrid_search:
                loop = asyncio.get_event_loop()
                sparse_results = await loop.run_in_executor(
                    None, embedder._embed_sparse_batch_sync, [query_text]
                )
                sparse_vector = sparse_results[0]

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
                entry = {
                    "rel_path": r.rel_path,
                    "symbol_name": r.symbol_name,
                    "symbol_type": r.symbol_type,
                    "start_line": r.start_line,
                    "end_line": r.end_line,
                    "language": r.language,
                    "content_preview": content[:300],
                }

                # Classify what this chunk represents
                route_paths = _extract_route_paths(content, r.rel_path)
                if route_paths:
                    entry["routes"] = route_paths
                    endpoints_by_coll[r.collection].append(entry)
                    continue

                code_urls = _extract_code_urls(content)
                config_paths, base_urls = _extract_config_urls(content)

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

        # Phase 3: Build adjacency summary
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
            services[coll] = {
                "endpoints_found": ep_count,
                "http_callers_found": caller_count,
                "configs_found": cfg_count,
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
                "total_edges": len(edges),
            },
        }
