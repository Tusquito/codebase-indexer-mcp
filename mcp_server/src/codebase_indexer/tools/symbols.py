# src/codebase_indexer/tools/symbols.py
"""MCP tool: search_symbols — symbol-only search with zero code content."""

from fastmcp import FastMCP

from codebase_indexer.config import Settings
from codebase_indexer.indexer.embedder import Embedder
from codebase_indexer.storage.qdrant import QdrantStorage


def register_search_symbols_tool(
    mcp: FastMCP, settings: Settings, storage: QdrantStorage, embedder: Embedder
) -> None:
    @mcp.tool(
        name="search_symbols",
        description=(
            "Token-efficient symbol lookup: runs the same hybrid search as "
            "search_codebase but returns ONLY symbol metadata — no code content. "
            "Returns: chunk_id, rel_path, symbol_name, symbol_type, start_line, "
            "end_line, language, score, collection. "
            "Use when you only need to know WHERE a symbol is defined/used, "
            "not what its code looks like. Call get_chunk for full content "
            "of any specific result. Saves ~90% tokens vs search_codebase "
            "for orientation and symbol-location tasks. "
            "'min_score' is a cosine threshold that only applies when hybrid search "
            "is disabled; in hybrid mode results are ranked by RRF fusion and bounded "
            "by 'top_k'."
        ),
    )
    async def search_symbols(
        query: str,
        top_k: int = 10,
        collection: str | None = None,
        collections: list[str] | None = None,
        language: str | None = None,
        min_score: float = 0.4,
    ) -> dict:
        if top_k > 30:
            top_k = 30

        primary = collection or settings.qdrant_collection
        target_collections = [primary]
        if collections:
            for c in collections:
                if c not in target_collections:
                    target_collections.append(c)

        dense_vector, sparse_vector = await embedder.embed_query(query)

        if len(target_collections) == 1:
            results = await storage.search(
                collection=target_collections[0],
                dense_vector=dense_vector,
                sparse_vector=sparse_vector,
                top_k=top_k,
                language=language,
                min_score=min_score,
            )
        else:
            results = await storage.search(
                collection=None,
                dense_vector=dense_vector,
                sparse_vector=sparse_vector,
                top_k=top_k,
                language=language,
                min_score=min_score,
                restrict_collections=target_collections,
            )

        return {
            "results": [
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
            ],
            "collections_searched": target_collections,
        }
