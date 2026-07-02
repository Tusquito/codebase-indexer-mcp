# 0013. Expose Qdrant retrieval via MCP for external agent orchestrators

- **Status:** Accepted
- **Date:** 2026-07-02
- **Deciders:** Maintainers
- **Related:** [Qdrant Agentic RAG with CrewAI](https://qdrant.tech/documentation/tutorials-build-essentials/agentic-rag-crewai-zoom/), [Qdrant CrewAI framework guide](https://qdrant.tech/documentation/frameworks/crewai/), [ADR 0012](0012-retrieval-only-rag-split.md)

## Context

[Qdrant’s CrewAI Agentic RAG tutorial](https://qdrant.tech/documentation/tutorials-build-essentials/agentic-rag-crewai-zoom/) builds a **multi-agent application** where:

- Qdrant stores domain embeddings (meeting transcripts)
- **CrewAI agents** with distinct roles query the vector store, analyze results, and coordinate tasks
- An external LLM (Claude) synthesizes natural-language answers
- Optional CrewAI **memory** (short-term, entity) also uses Qdrant via custom `RAGStorage`

The codebase-indexer MCP server occupies a similar **knowledge-plane** role for **source code**, but clients are general-purpose AI agents (Cursor, Claude Code, custom CrewAI crews) rather than a single embedded CrewAI app.

Today we expose retrieval through MCP tools instead of Python `RAGStorage` subclasses. Agent orchestration, role definitions, task graphs, and CrewAI memory collections remain **outside** the server—by design ([ADR 0012](0012-retrieval-only-rag-split.md)).

We need a decision on whether to:

1. Embed CrewAI (or similar) agents inside the MCP server, or
2. Stay a **protocol-level knowledge base** that any orchestrator can call

### Constraints

- MCP is the primary integration surface (HTTP streamable + stdio proxy)
- One Qdrant collection per workspace folder; hybrid vectors indexed by this server
- Self-hosted default; no requirement for users to adopt CrewAI
- Agent memory (conversation traces, entity notes) is a different data domain than code chunks

## Decision

We will **not embed CrewAI, LangGraph, or other orchestration frameworks** in the MCP server. Instead, we **standardize on MCP tools as the agent-facing retrieval API**—functionally equivalent to the tutorial’s vector-search tool step, but usable by Cursor agents today and by CrewAI/custom agents via MCP client adapters.

### Responsibility split

| Layer | Owner | Examples |
|-------|-------|----------|
| Code indexing & hybrid search | MCP server | `index_codebase`, `search_codebase`, `search_symbols` |
| Multi-agent roles, tasks, crews | External orchestrator | CrewAI `Agent` / `Task`, Cursor agent loop |
| LLM completion & synthesis | External LLM | Claude, GPT, local models |
| Agent session / entity memory | External app (optional) | CrewAI `QdrantStorage("entity")` in a **separate** collection namespace—not code index collections |

### MCP tool set as “agent tools”

Map tutorial concepts to existing MCP tools:

| Agentic RAG need | MCP tool |
|------------------|----------|
| Semantic search over corpus | `search_codebase` |
| Locate symbols before reading bodies | `search_symbols` |
| Fetch full implementation | `get_chunk` |
| Orient in unfamiliar repo | `get_collection_summary`, `get_file_outline` |
| Multi-repo / service questions | `find_cross_references`, `map_service_dependencies` |
| Graph-augmented context (future) | `expand_search_context` per [ADR 0002](0002-graphrag-neo4j-qdrant.md) |

### CrewAI integration path (external, documented—not in-server)

Teams that want the tutorial’s CrewAI pattern against **their codebase** should:

1. Run this MCP server + Qdrant (existing compose)
2. Give CrewAI agents an MCP tool that calls `search_codebase` / `get_chunk` (HTTP JSON-RPC or stdio proxy)
3. Keep CrewAI memory collections **separate** from `codeindexer_*` code collections (different collection names; avoid mixing agent chat memory with AST chunks)

We will **not** ship a first-party `crewai` Python dependency or in-process `RAGStorage` adapter in `mcp_server/`.

### Discord / CAMEL-AI variant (same decision)

The [Discord RAG bot tutorial](https://qdrant.tech/documentation/tutorials-build-essentials/agentic-rag-camelai-discord/) wraps retrieval + CAMEL-AI + Discord I/O. That chat-platform pattern is **out of scope** for the same reasons: clients provide the UI and agent loop; MCP provides retrieval ([ADR 0012](0012-retrieval-only-rag-split.md)).

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **MCP tools for all orchestrators (chosen)** | Works with Cursor today; vendor-neutral; matches MCP spec | CrewAI users need a thin MCP client wrapper |
| **In-process CrewAI module** | Mirrors tutorial repo layout | Heavy deps; duplicates client agents; two agent loops |
| **Ship `QdrantStorage` adapter in-repo** | Drop-in for CrewAI Python apps | Bypasses MCP auth/index pipeline; splits embedding config |
| **Unified memory + code in one Qdrant collection** | Single cluster | Pollutes code index; complicates re-index and ACLs |

## Consequences

### Positive

- Cursor and other MCP clients already consume the agentic RAG retrieval step without custom Python
- Code collections stay deterministic (tree-sitter, git-aware re-index) separate from ephemeral agent memory
- Tutorial readers can map “vector search tool” → MCP tool list 1:1
- Optional GraphRAG ([ADR 0002](0002-graphrag-neo4j-qdrant.md)) extends agent context without CrewAI coupling

### Negative / trade-offs

- No turnkey CrewAI sample repo in this monorepo; integration is documented, not packaged
- CrewAI memory still requires users to manage separate Qdrant collections and embedders
- Agents must be configured to call MCP tools (skills/rules); not automatic like embedded `vector_retriever.query()`

### Neutral / follow-ups

- Add `docs/INTEGRATIONS.md` section: “CrewAI / custom agents via MCP” with HTTP endpoint example
- Evaluate official MCP tool schema export for CrewAI tool registration—documentation only

## Implementation notes

### Affected paths

- Documentation: `README.md`, `docs/ARCHITECTURE.md`, optional `docs/INTEGRATIONS.md`
- No new Python packages under `mcp_server/` for CrewAI/CAMEL

### Rollout

- Accepted architectural stance; existing MCP tool surface is the implementation

### Re-index

**No**

## Validation

- All retrieval capabilities exposed as registered MCP tools in `main.py`
- No `crewai`, `camel-ai`, or `discord.py` imports in `mcp_server/pyproject.toml`
- Success: an external CrewAI agent can answer code questions using only MCP tool calls against indexed collections
