# src/codebase_indexer/tools/graph_search.py
"""MCP tool: expand_search_context (ADR 0002 Phase 3, opt-in GraphRAG).

Runs the existing hybrid search to get seed chunks, then issues one bounded
Cypher neighborhood query against Neo4j (chunk_id-only seeding — no
``graph_node_ids`` payload reads), hydrates related chunk payloads from Qdrant,
and returns a structured ``GraphContext`` dict. No LLM answer is generated.

Registered only when ``GRAPH_ENABLED=true``; with the flag off the tool is
absent and nothing else changes.
"""

from __future__ import annotations

from typing import TYPE_CHECKING

from fastmcp import FastMCP

from codebase_indexer.tools.search_common import resolve_collections, run_search
from codebase_indexer.telemetry.metrics import observe_tool

if TYPE_CHECKING:
    from codebase_indexer.context import AppContext


def _empty_context(seeds: list[dict] | None = None) -> dict:
    return {
        "nodes": [],
        "edges": [],
        "related_chunks": [],
        "seeds": seeds or [],
    }


def register_expand_search_context_tool(mcp: FastMCP, ctx: "AppContext") -> None:
    settings = ctx.settings
    storage = ctx.storage
    embedder = ctx.embedder
    graph_storage = ctx.graph_storage

    @mcp.tool(
        name="expand_search_context",
        description=(
            "Graph-augmented retrieval (opt-in, only present when GRAPH_ENABLED=true). "
            "Runs the same hybrid search as search_codebase to find seed chunks, then "
            "expands 1..GRAPH_MAX_HOPS hops in the Neo4j code graph (CALLS, HTTP_CALLS, "
            "DECLARES_ENDPOINT, DEFINES, IN_FILE, ...) capped by GRAPH_MAX_NODES. Returns "
            "a structured graph context (nodes, edges, related_chunks, seeds) — NOT an "
            "answer. Use it when a single search cannot surface structural neighbors "
            "(callers/callees, endpoints, cross-file relationships). 'graph_hops' defaults "
            "to GRAPH_MAX_HOPS and is clamped to [1, GRAPH_MAX_HOPS]. top_k is capped at 20."
        ),
    )
    @observe_tool("expand_search_context")
    async def expand_search_context(
        query: str,
        top_k: int = 5,
        collection: str | None = None,
        collections: list[str] | None = None,
        graph_hops: int | None = None,
        language: str | None = None,
        min_score: float = 0.5,
        max_content_chars: int | None = None,
    ) -> dict:
        if top_k > 20:
            top_k = 20

        target_collections = resolve_collections(
            collection or settings.qdrant_collection, collections
        )

        results = await run_search(
            storage,
            embedder,
            query,
            target_collections,
            top_k,
            language,
            min_score,
        )

        seeds = [
            {
                "chunk_id": r.chunk_id,
                "score": round(r.score, 4),
                "collection": r.collection,
                "rel_path": r.rel_path,
                "symbol_name": r.symbol_name,
                "symbol_type": r.symbol_type,
                "start_line": r.start_line,
                "end_line": r.end_line,
                "language": r.language,
            }
            for r in results
        ]

        # Graph disabled / unavailable → guarded empty context (tool should not
        # normally be registered in this case, but stay defensive).
        if graph_storage is None or not graph_storage.enabled:
            return _empty_context(seeds)

        seed_chunk_ids = [r.chunk_id for r in results if r.chunk_id]
        if not seed_chunk_ids:
            return _empty_context(seeds)

        hops = graph_hops if graph_hops is not None else settings.graph_max_hops
        hops = max(1, min(int(hops), settings.graph_max_hops))

        expansion = await graph_storage.expand_subgraph(
            chunk_ids=seed_chunk_ids,
            max_hops=hops,
            max_nodes=settings.graph_max_nodes,
        )

        related_chunks = []
        for cid in expansion.related_chunk_ids:
            coll = expansion.related_chunk_collections.get(cid)
            if coll:
                payload = await storage.get_chunk_by_id(coll, cid)
            else:
                payload = await storage.find_chunk_by_id(cid)
            if not payload:
                continue
            content = payload.get("content", "") or ""
            truncated = False
            if max_content_chars is not None and len(content) > max_content_chars:
                content = content[:max_content_chars]
                truncated = True
            item = {
                "chunk_id": cid,
                "collection": coll or payload.get("collection"),
                "rel_path": payload.get("rel_path"),
                "symbol_name": payload.get("symbol_name"),
                "symbol_type": payload.get("symbol_type"),
                "start_line": payload.get("start_line"),
                "end_line": payload.get("end_line"),
                "language": payload.get("language"),
                "content": content,
            }
            if truncated:
                item["content_truncated"] = True
            related_chunks.append(item)

        return {
            "nodes": [
                {"labels": n.labels, "key": n.key, "props": n.props}
                for n in expansion.nodes
            ],
            "edges": [
                {"type": e.type, "from": e.from_key, "to": e.to_key}
                for e in expansion.edges
            ],
            "related_chunks": related_chunks,
            "seeds": seeds,
            "collections_searched": target_collections,
            "graph_hops": hops,
        }
