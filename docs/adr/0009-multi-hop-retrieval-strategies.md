# 0009. Multi-hop code retrieval strategies

- **Status:** Accepted (phase 1; phase 2 merged)
- **Date:** 2026-07-02
- **Deciders:** Maintainers
- **Related:** [Query Decomposition for Multi-Hop Questions](https://qdrant.tech/documentation/improve-search/query-decomposition/), [GraphRAG with Neo4j](https://qdrant.tech/documentation/examples/graphrag-qdrant-neo4j/), [ADR 0002](0002-graphrag-neo4j-qdrant.md), [ADR 0005](0005-mcp-retrieval-connector.md)

## Context

Many code questions are **multi-hop**: answering them requires chaining facts from separate chunks or files. Examples:

- “Which HTTP client does `BillingService` use to call the payment API defined in repo B?”
- “Where is the env var read that configures the timeout set in `docker-compose.yml`?”

Qdrant’s [Improve Search](https://qdrant.tech/documentation/improve-search/) tutorial [Query Decomposition for Multi-Hop Questions](https://qdrant.tech/documentation/improve-search/query-decomposition/) addresses this with an **LLM-driven loop**:

1. Search for the original question
2. LLM reads results and emits the next sub-question (or `DONE`)
3. Search again; repeat
4. **Fuse all hops** with RRF before the final answer step

The tutorial notes reranking and single-pass fusion **cannot recover evidence never retrieved** — decomposition adds queries specifically to reach missing hops.

[ADR 0002](0002-graphrag-neo4j-qdrant.md) proposes a complementary approach: **vector search → graph expansion** over deterministic code relationships (imports, calls, HTTP edges) without LLM-constructed sub-questions.

[ADR 0005](0005-mcp-retrieval-connector.md) places **all LLM reasoning in the MCP client**, not the indexer server.

We need a unified decision on multi-hop retrieval: what the server provides vs what the client orchestrates.

## Decision

We will support multi-hop code retrieval through **three complementary mechanisms**, ordered by server involvement:

### 1. Client-orchestrated query decomposition (primary, no new server code)

Document and encourage the Qdrant decomposition pattern for MCP clients:

- Client calls `search_codebase` with the user question
- Client LLM proposes a sub-question from returned chunks (or uses `search_symbols` / `get_file_outline` for zero-embed steps)
- Client searches again; client-side RRF merges chunk IDs across hops
- Client synthesizes the final answer

The MCP server **does not** implement an in-server decomposition loop, sub-question generator, or hop fuse endpoint in phase 1 — consistent with [ADR 0005](0005-mcp-retrieval-connector.md).

**Server responsibilities:** stable `chunk_id`, hybrid search per hop, token-efficient tools to reduce embedding cost between hops.

### 2. Deterministic tool chaining (single-hop helpers, already partial)

Existing tools cover **one hop** of structured navigation:

- `search_symbols` → `get_chunk` / `get_file_outline`
- `find_cross_references` — definition / import / usage / HTTP call edges
- `map_service_dependencies` — service-level graph heuristics

Clients compose these for multi-hop paths without LLM decomposition when the query maps to known relation types.

### 3. Optional GraphRAG expansion (server-side multi-hop context)

When [ADR 0002](0002-graphrag-neo4j-qdrant.md) is enabled, `expand_search_context` returns vector seeds plus **1–2 hop subgraphs** (calls, imports, HTTP) in one MCP call — reducing client round-trips for relationship-centric questions.

GraphRAG and query decomposition are **orthogonal**:

| Approach | Best for | LLM in loop | Server cost |
|----------|----------|-------------|-------------|
| Query decomposition | Facts not linked in graph (narrative docs, comments, config prose) | Client | N embed queries per hop |
| Graph expansion | Structural code edges (call chains, imports, endpoints) | None at retrieval | One search + Cypher |
| Tool chaining | Known symbol / xref queries | Optional | 2–4 tool calls |

### Fusion guidance for clients

Per the decomposition tutorial: when merging multi-hop results, **fuse all hops with RRF**, not only the last search — earlier hops often hold bridging evidence (e.g. class definition linking interface to implementation).

Optional future server endpoint `search_multi_hop` (server runs decomposition with client-provided sub-questions only) is **out of scope** — would require LLM keys in server.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Client decomposition + optional GraphRAG (chosen)** | Matches Qdrant tutorial and [ADR 0005](0005-mcp-retrieval-connector.md); graph for code structure | Client quality varies; more client prompts |
| **In-server decomposition loop** | One-shot MCP tool | LLM keys in indexer; latency; violates [ADR 0005](0005-mcp-retrieval-connector.md) |
| **GraphRAG only** | No LLM at retrieval | Misses prose/config hops not in graph schema |
| **Single-pass hybrid + rerank only** | Simple | Cannot fix missing-hop recall ([decomposition tutorial](https://qdrant.tech/documentation/improve-search/query-decomposition/)) |
| **HyDE / hypothetical document embedding** | Better semantic recall | Extra LLM call; deferred |

## Consequences

### Positive

- Applies Qdrant Improve Search multi-hop guidance without compromising retrieval-only server design
- GraphRAG ([ADR 0002](0002-graphrag-neo4j-qdrant.md)) targets structural hops; decomposition covers semantic gaps
- Evaluable: compare single-pass vs multi-hop `recall@10` on a multi-hop golden subset ([ADR 0007](0007-ranx-retrieval-evaluation.md))

### Negative / trade-offs

- Decomposition quality depends on client model and tool-use skill
- Multi-hop client loops multiply embedding cost (each `search_codebase` embeds query)
- Without GraphRAG, clients must manually chain 3+ tool calls for long paths
- No server-side enforcement that clients fuse all hops

### Neutral / follow-ups

- ~~MCP server instructions: document decomposition + RRF merge pattern with example client pseudo-flow~~ → done (phase 1)
- ~~Golden set tag: `multi_hop: true` queries for [ADR 0007](0007-ranx-retrieval-evaluation.md)~~ → `multi_hop` tag in golden set (4 queries)
- `expand_search_context` ([ADR 0002](0002-graphrag-neo4j-qdrant.md)) as preferred path when `GRAPH_ENABLED=true`
- Automated 2-hop client script for eval comparison vs single-pass on `multi_hop` tag slice

## Implementation notes

### Affected paths (documentation-first phase)

- MCP tool instructions in `main.py` — multi-hop playbook
- `docs/SEARCH_BEHAVIOR.md` — decomposition vs GraphRAG decision tree
- `mcp_server/benchmarks/fixtures/golden_queries.jsonl` — multi-hop labeled queries ([ADR 0007](0007-ranx-retrieval-evaluation.md))

No server code required for phase 1 (client decomposition documentation). Phase 1 delivered:

- [`main.py`](../mcp_server/src/codebase_indexer/main.py) `_INSTRUCTIONS` — multi-hop playbook
- [`docs/SEARCH_BEHAVIOR.md`](../SEARCH_BEHAVIOR.md#multi-hop-retrieval) — strategy decision tree + client RRF merge
- [`golden_queries.jsonl`](../mcp_server/benchmarks/fixtures/golden_queries.jsonl) — four `multi_hop`-tagged queries

### Rollout

Documentation and golden-set tags; default behavior unchanged.

### Re-index

**No** for documentation phase. GraphRAG path per [ADR 0002](0002-graphrag-neo4j-qdrant.md).

## Validation

- Manual: Cursor/Claude client answers a multi-hop fixture question using 2+ `search_codebase` calls + client-side merge
- Golden set: multi-hop queries show higher `recall@10` with documented 2-hop client script vs single-pass
- With `GRAPH_ENABLED=true`, `expand_search_context` reduces tool calls for call-chain fixture vs decomposition-only

Success criteria:

- Published guidance references Qdrant query decomposition tutorial explicitly
- Multi-hop golden queries measurable in [ADR 0007](0007-ranx-retrieval-evaluation.md) harness
