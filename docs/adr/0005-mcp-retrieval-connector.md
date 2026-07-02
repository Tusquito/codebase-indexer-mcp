# 0005. MCP as external RAG retrieval connector

- **Status:** Accepted
- **Date:** 2026-07-02
- **Deciders:** Maintainers
- **Related:** [Implement custom connector for Cohere RAG](https://qdrant.tech/documentation/examples/cohere-rag-connector/), [Basic RAG / FastEmbed quickstart](https://qdrant.tech/articles/fastembed/)

## Context

Most Qdrant “Build Prototype” samples implement **full RAG pipelines**: retrieve context from Qdrant, assemble a prompt, call an LLM, return a natural-language answer. Examples include:

- [Hybrid Search on PDF Manuals](https://qdrant.tech/documentation/examples/hybrid-search-llamaindex-jinaai/) — LlamaIndex `RetrieverQueryEngine` + Mixtral synthesis
- [Implement custom connector for Cohere RAG](https://qdrant.tech/documentation/examples/cohere-rag-connector/) — FastAPI `/search` connector consumed by Command-R
- Employee onboarding, customer support, blog chatbot, and medical chatbot tutorials — LangChain/Haystack/DSPy end-to-end stacks

The Cohere connector pattern is especially relevant: expose Qdrant search as an **HTTP tool** the LLM invokes; the connector returns structured documents (`title`, `text`); the **host model** decides how to use them. The tutorial notes connectors may implement **hybrid search** and that result count is a connector policy, not an LLM parameter.

Our product is a **Model Context Protocol (MCP) server** consumed by Cursor, Claude, Copilot CLI, and similar clients. Those clients already run the LLM. Duplicating generation inside the MCP server would:

- Require model API keys inside the indexer container
- Couple retrieval latency to generation latency
- Duplicate capabilities the host client provides
- Conflict with the self-hosted, no-cloud-LLM-default goal

We need an ADR that positions the MCP server relative to Qdrant’s RAG prototypes.

## Decision

We will implement **retrieval-only** MCP tools that return structured code context (chunks, symbols, outlines, cross-references). The connected AI client performs all reasoning, synthesis, and answer generation — analogous to the Cohere custom connector, but via MCP tool calls instead of a proprietary connector API.

The MCP server owns:

- Indexing (scan → chunk → embed → upsert)
- Query embedding and Qdrant search (including hybrid RRF per [ADR 0003](0003-hybrid-search-rrf-default.md))
- Zero-embedding orientation tools (`get_collection_summary`, `get_file_outline`, `get_chunk`)
- Optional graph expansion ([ADR 0002](0002-graphrag-neo4j-qdrant.md))

The MCP server does **not** own:

- Prompt templates with “answer the query” instructions
- LLM API calls (OpenAI, Cohere, Mixtral, etc.)
- Citation formatting inline in generated prose
- Confidence / “I don’t know” response policy

### Tool design vs Cohere connector

| Cohere connector | MCP equivalent |
|------------------|----------------|
| `POST /search { query }` | `search_codebase(query, collection, top_k, …)` |
| `{ results: [{ title, text }] }` | `{ results: [{ rel_path, symbol_name, content, score, … }] }` |
| Connector picks `limit` | Tool caps `top_k` (20 / 30) — connector policy |
| Hybrid search optional | Hybrid default ([ADR 0003](0003-hybrid-search-rrf-default.md)) |
| Bearer auth on connector | Optional `MCP_AUTH_TOKEN` bearer middleware |

### Transport

Primary: **HTTP streamable MCP** (`codeindexer_mcp :8000`). Fallback: stdio proxy sidecar. Both expose the same tool surface — similar to exposing the Cohere connector via public URL or ngrok tunnel.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Retrieval-only MCP (chosen)** | No model keys in indexer; client picks LLM; composable with any host; matches Cohere connector separation | Client must chain tools; no single “ask” endpoint |
| **In-server RAG chat tool** | One-shot UX like PDF chatbot tutorials | Model keys, cost, latency, provider lock-in |
| **Cohere connector compatibility layer** | Direct Command-R integration | Proprietary API; redundant with MCP; not self-hosted |
| **LlamaIndex query engine inside MCP** | Rich reranking/synthesis | Heavy dependency; still needs LLM config |
| **Return only raw Qdrant payloads** | Minimal code | Poor ergonomics; no token-efficient orientation layer |

## Consequences

### Positive

- Same separation of concerns as Qdrant’s Cohere connector tutorial — search service vs generation
- Works with any MCP-capable client and model (local or cloud)
- Token-efficient tool layering (`get_collection_summary` → `search_symbols` → `get_chunk`) reduces embedding cost vs always returning full RAG context
- Hybrid search and future GraphRAG enrich connector responses without changing client LLM

### Negative / trade-offs

- No turnkey “chat with your codebase” endpoint — users configure MCP + client
- Quality depends on client tool-use behavior; server cannot enforce grounding prompts
- Cannot offer Cohere-style inline citations without client cooperation

### Neutral / follow-ups

- OpenAI-compatible `/search` shim for non-MCP clients deferred
- Optional `search_codebase` response format preset (minimal vs full) already partially via `max_content_chars`
- Streaming aggregated context for long multi-hop graph results deferred ([ADR 0002](0002-graphrag-neo4j-qdrant.md))

## Implementation notes

### Affected paths

- `mcp_server/src/codebase_indexer/main.py` — FastMCP tool registration
- `mcp_server/src/codebase_indexer/tools/search.py`, `symbols.py`, `chunk.py`, `summary.py`
- `mcp_server/src/codebase_indexer/stdio_proxy.py` — HTTP fallback
- Optional bearer auth middleware in `main.py`

### Rollout

Default unchanged.

### Re-index

**No**.

## Validation

- MCP tool schemas documented in server instructions and README
- Integration tests for `search_codebase` result shape
- Manual: Cursor/Claude client retrieves chunks and synthesizes answers without server-side LLM env vars

Success criteria:

- Server starts with zero LLM API keys configured
- Search tools return structured JSON usable as RAG context by any client
- Hybrid search improves retrieval vs dense-only for identifier queries ([ADR 0003](0003-hybrid-search-rrf-default.md))
