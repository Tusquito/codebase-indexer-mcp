# 0012. Keep MCP as retrieval-only RAG layer

> **Historical context:** Embedding stack references (Ollama dense, ONNX dense) predate [ADR 0025](0025-huggingface-tei-dense-embedding.md). Production dense is **TEI HTTP** today.

- **Status:** Accepted
- **Date:** 2026-07-02
- **Deciders:** Maintainers
- **Related:** [Qdrant 5-Minute RAG with DeepSeek](https://qdrant.tech/documentation/tutorials-build-essentials/rag-deepseek/), [ADR 0005](0005-mcp-retrieval-connector.md), [ADR 0011](0011-ollama-only-dense-embedding.md)

## Context

[Qdrant’s DeepSeek RAG tutorial](https://qdrant.tech/documentation/tutorials-build-essentials/rag-deepseek/) demonstrates the canonical RAG loop:

1. Embed documents with Ollama dense + BM25 sparse and upsert into Qdrant
2. Embed the user question and run `query_points` for top-*k* context
3. Concatenate retrieved payloads into a **context block**
4. Build a **metaprompt** (role + question + context + “don’t hallucinate” guardrails)
5. Call an external LLM (DeepSeek) for the final answer

The codebase-indexer MCP server already implements steps 1–3 for **source code**:

- Ollama dense + BM25 sparse → Qdrant hybrid collections per workspace folder
- MCP tools (`search_codebase`, `get_chunk`, `search_symbols`, …) return ranked chunks and metadata
- Connected AI clients (Cursor, Claude, Copilot CLI) perform steps 4–5 in their own context windows

We need an explicit decision because the tutorial bundles retrieval and generation in one Python process. Contributors may assume we should add LLM APIs, metaprompt templates, or answer synthesis inside the MCP server—patterns that conflict with self-hosting, bearer-auth simplicity, and the product’s role as a **tool provider** rather than a chat application.

### Constraints

- No mandatory external LLM API keys in the MCP container
- Default deployment remains Qdrant + MCP + optional cron
- Hybrid search (dense + sparse RRF) is the primary retrieval quality lever
- AI clients already own system prompts, tool orchestration, and response formatting

## Decision

We will **implement only the retrieval half of RAG** inside the MCP server and **deliberately omit in-server LLM generation, metaprompt assembly, and answer evaluation**.

The MCP returns structured retrieval results (code chunks, symbols, outlines, cross-references). The connected AI client is responsible for:

- Metaprompt / system-instruction design (equivalent to the tutorial’s “software architect” role block)
- Grounding answers in returned tool output
- Refusing when context is insufficient (equivalent to the tutorial’s “I don’t know” behavior)

This matches the tutorial’s architectural insight—convert knowledge tasks into language tasks **using retrieved context**—without coupling the server to a specific LLM vendor.

### In scope (already implemented)

| Tutorial step | MCP equivalent |
|---------------|----------------|
| FastEmbed → Qdrant upsert | `indexer/pipeline.py` → `Embedder` → `QdrantStorage.upsert` |
| Semantic query → top-*k* | `search_codebase`, `search_symbols` via `search_common.py` |
| Context concatenation | Client merges tool JSON/text; optional `max_content_chars` truncation on tools |
| Hybrid retrieval quality | `HYBRID_SEARCH=true` dense + sparse RRF (stronger than tutorial’s dense-only) |

### Out of scope (permanent)

- DeepSeek, OpenAI, Anthropic, or other completion API integration in MCP
- Server-side metaprompt templates or `rag(question) -> str` answer functions
- Automated RAG accuracy evaluation harness inside the server (benchmarks stay in `benchmarks/bench.py` for latency/recall, not LLM judge loops)

### Optional future (client-side or docs only)

- Document a **recommended metaprompt pattern** for Cursor rules / agent skills referencing MCP tools (not server code)
- Client-side “cite chunk IDs” conventions in tool output formatting

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Retrieval-only MCP (chosen)** | Self-hosted; vendor-neutral; matches MCP tool model; tutorial steps 1–3 map cleanly | Clients must implement grounding; no single-command `rag()` demo |
| **Full in-process RAG like tutorial** | One script demonstrates end-to-end accuracy | Requires LLM API keys in server; duplicates client reasoning; breaks self-hosted default |
| **Optional LLM backend plugin** | Could offer opt-in answer synthesis | Scope creep; auth/billing complexity; every client already has an LLM |
| **Return pre-formatted metaprompt strings from tools** | Slightly easier client integration | Leaks prompt policy into server; harder to customize per client |

## Consequences

### Positive

- Architecture aligns with Qdrant’s RAG data plane while staying MCP-native
- No LLM vendor lock-in or API key management in Docker Compose
- Hybrid search exceeds the tutorial’s dense-only retrieval for code (identifiers, exact tokens)
- Ollama dense + BM25 sparse retrieval ([ADR 0011](0011-ollama-only-dense-embedding.md); backend facade from [ADR 0001](0001-pluggable-embed-backends.md)) improves the data layer without touching generation

### Negative / trade-offs

- No bundled “before/after RAG accuracy” demo; users must evaluate in their AI client
- Clients that don’t call search tools before answering may hallucinate—mitigated by Cursor rules / skills, not server enforcement
- Tutorial newcomers may expect a single `rag()` function; README must clarify the split

### Neutral / follow-ups

- Add a “RAG split” subsection to README linking this ADR and the Qdrant tutorial
- Consider `search_codebase` response field `citation_hint` (chunk_id list) for client grounding—optional, not required

## Implementation notes

### Affected paths

- No code change required; documents existing design
- Cross-links: `docs/ARCHITECTURE.md`, `README.md`, MCP tool docstrings in `tools/search.py`

### Rollout

- Documentation-only ADR; default behavior unchanged

### Re-index

**No**

## Validation

- MCP tools return ranked chunks with payloads; no completion API calls in `mcp_server/src/`
- Existing search and index tests pass unchanged
- Success: a client can reproduce the tutorial’s metaprompt flow using `search_codebase` output without server-side LLM code
