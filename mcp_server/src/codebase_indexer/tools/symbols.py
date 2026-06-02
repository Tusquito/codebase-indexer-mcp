# src/codebase_indexer/tools/symbols.py
"""MCP tool: search_symbols — symbol-only search with zero code content."""

import asyncio

from fastmcp import FastMCP

from codebase_indexer.config import Settings
from codebase_indexer.indexer.embedder import Embedder
from codebase_indexer.storage.qdrant import QdrantStorage


def register_search_symbols_tool(
    mcp: FastMCP, settings: Settings, storage: QdrantStorage
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
            "for orientation and symbol-location tasks."
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

        embedder = Embedder(
            model=settings.embed_model,
            vector_size=settings.vector_size,
            hybrid=settings.hybrid_search,
        )

        dense_vector = (await embedder.embed_batch_dense([query]))[0]

        sparse_vector = None
        if settings.hybrid_search:
            loop = asyncio.get_event_loop()
            sparse_results = await loop.run_in_executor(
                None, embedder._embed_sparse_batch_sync, [query]
            )
            sparse_vector = sparse_results[0]

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
