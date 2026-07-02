# 0006. Explicit FastEmbed pipeline over qdrant-client convenience API

- **Status:** Accepted
- **Date:** 2026-07-02
- **Deciders:** Maintainers
- **Related:** [FastEmbed + Qdrant article](https://qdrant.tech/articles/fastembed/), [qdrant-client quickstart / Basic RAG notebook](https://github.com/qdrant/qdrant-client/blob/master/docs/source/quickstart.ipynb), [ADR 0011](0011-ollama-only-dense-embedding.md)

## Context

Qdrant’s introductory examples — including the **Basic RAG** notebook, **Intro to Semantic Search**, and the [FastEmbed article](https://qdrant.tech/articles/fastembed/) — promote a high-level `qdrant-client` API:

```python
client.add(collection_name="demo", documents=docs)
results = client.query(collection_name="demo", query_text="...")
```

The client internally invokes FastEmbed, generates IDs, and upserts points. This is ideal for prototypes and single-vector dense RAG.

The codebase-indexer MCP server has requirements that exceed that convenience API:

| Requirement | Convenience API | Our need |
|-------------|-----------------|----------|
| Vector types | Single dense default | Named `dense` + `sparse` hybrid vectors |
| Chunking | Raw document strings | Tree-sitter AST chunks + sliding window + import context |
| Embedding control | Fixed DefaultEmbedding | Ollama dense HTTP + sparse BM25 ONNX; batch sizing, memory guards, idle release |
| Batch semantics | Opaque | Adaptive batch sizing, memory guards, idle release |
| Payload schema | Generic metadata dict | `chunk_id`, `symbol_name`, line range, `file_sha256`, etc. |
| Incremental index | Full re-add typical | mtime/SHA-256 delta, stale chunk deletion |
| Index-time tuning | None | Deferred HNSW during bulk, quantization, on-disk vectors |

We need an ADR documenting why we use an explicit pipeline instead of `client.add` / `client.query`.

## Decision

We will use an **explicit indexing and search pipeline** with a dedicated `Embedder` facade and `QdrantStorage` client, rather than `qdrant-client`’s built-in FastEmbed `add`/`query` shortcuts.

Stack:

1. **Chunk** — `indexer/chunker.py` (tree-sitter + fallbacks)
2. **Embed** — `indexer/embedder.py` → Ollama dense + sparse BM25 backends ([ADR 0011](0011-ollama-only-dense-embedding.md))
3. **Store** — `storage/qdrant.py` with manual `upsert` batches and hybrid `query_points` + prefetch RRF ([ADR 0003](0003-hybrid-search-rrf-default.md))
4. **Search** — `tools/search_common.py` embeds queries then calls `QdrantStorage.search`

We still standardize on **fastembed ONNX for sparse BM25** — the same library Qdrant documents — but invoke it through our backends, not through the client’s opaque wrapper. **Dense vectors come from Ollama HTTP** ([ADR 0011](0011-ollama-only-dense-embedding.md)).

### What we borrow from Basic RAG / semantic search prototypes

- **Self-hosted embeddings** — no query-time cloud embedding API by default
- **Cosine dense vectors** with configurable model/dimension tables in `config.py`
- **Semantic search** as the primary retrieval primitive for natural-language code questions
- **Metadata-rich payloads** stored alongside vectors for downstream RAG context

### What we deliberately omit

- `QdrantClient.set_model()` / `Document(text=…, model=…)` query helpers — replaced by explicit query embedding
- Automatic collection creation from `add()` — replaced by `ensure_collection` with hybrid schema validation
- In-client embedding model singleton — replaced by backend-owned lifecycle with `release_models`

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Explicit pipeline (chosen)** | Full control over hybrid, chunking, batches, backends | More code; must track qdrant-client API changes manually |
| **`qdrant-client[fastembed]` add/query** | Minimal LOC; matches tutorials | No hybrid sparse; no AST chunking; dense tied to client DefaultEmbedding |
| **LlamaIndex VectorStoreIndex** | Rich RAG ecosystem | Heavy framework; LLM-oriented; conflicts with MCP retrieval-only ([ADR 0005](0005-mcp-retrieval-connector.md)) |
| **LangChain Qdrant vector store** | Popular integrations | Same framework weight; less control over index tuning |
| **Hybrid via two convenience collections** | Could fake dense+sparse | Duplicate payloads; fusion outside Qdrant; inconsistent IDs |

## Consequences

### Positive

- Hybrid search, quantization, and on-disk vector tuning are first-class
- Ollama dense embedding without forking qdrant-client
- Chunk schema stable for cross-references, GraphRAG linking, and cron incremental index
- Benchmarks (`benchmarks/bench.py`) can A/B payload indexes and search params reproducibly

### Negative / trade-offs

- New contributors familiar with Basic RAG notebook cannot copy-paste `client.add` — must learn pipeline modules
- We maintain embedding batch logic that qdrant-client would otherwise handle
- Must manually align vector dimensions with `ensure_collection` recreation logic

### Neutral / follow-ups

- Evaluate qdrant-client helpers for **non-hybrid** dev/test fixtures only
- If qdrant-client adds first-class hybrid `add`/`query`, reassess duplication — unlikely to cover AST chunking

## Implementation notes

### Affected paths

- `mcp_server/src/codebase_indexer/indexer/pipeline.py` — orchestrates scan → chunk → embed → upsert
- `mcp_server/src/codebase_indexer/indexer/embedder.py` — facade
- `mcp_server/src/codebase_indexer/indexer/backends/` — Ollama dense + fastembed sparse BM25
- `mcp_server/src/codebase_indexer/storage/qdrant.py` — low-level Qdrant API

### Rollout

Default unchanged.

### Re-index

**No** for this decision alone.

## Validation

- `test_ollama_dense_backend.py` — Ollama HTTP batching and truncation
- Sparse BM25 tests via `onnx_sparse` / truncation helpers
- `test_storage_integration.py` — end-to-end upsert + hybrid search
- Default Docker path uses Ollama dense + BM25 sparse (not qdrant-client text shortcuts)

Success criteria:

- Index + search works without calling `QdrantClient.add` or `QdrantClient.query` text shortcuts
- Changing `OLLAMA_EMBED_MODEL` does not require qdrant-client API changes
- Hybrid collections created exclusively through `ensure_collection`
