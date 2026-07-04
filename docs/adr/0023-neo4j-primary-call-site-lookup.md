# 0023. Move call-site lookup from Qdrant callees to Neo4j CALLS

- **Status:** Accepted (phase 1 — Symbol-unified CALLS + Neo4j caller query + dual-read routing)
- **Date:** 2026-07-04
- **Deciders:** Maintainers
- **Related:** [ADR 0002](0002-graphrag-neo4j-qdrant.md), [ADR 0005](0005-mcp-retrieval-connector.md), [ADR 0009](0009-multi-hop-retrieval-strategies.md)

## Context

`find_cross_references` Path D resolves precise call sites when the client passes `member` (and optionally `receiver`). Today that path calls `QdrantStorage.find_callers_in_collections`, which scrolls collections with a keyword filter on the **`callees` payload field**:

```text
callees: ["save", "orderRepo.findById", …]   ← bare method + receiver.method tokens
```

Indexing extracts callees in `chunker._extract_callees`, stores them on every Qdrant point, and creates a keyword payload index on `callees` when `PAYLOAD_INDEXES=true`.

[ADR 0002](0002-graphrag-neo4j-qdrant.md) Phase 1 (shipped) **also** writes the same tokens as `(Chunk)-[:CALLS]->(Symbol)` edges in Neo4j via `graph_writer.py`. That ADR treats callees as a **shared extractor input** for dual stores — not as a migration away from Qdrant:

| Store | Call-site data today | Query path today |
|-------|----------------------|------------------|
| Qdrant | `callees` keyword payload + index | `find_callers_in_collections` scroll filter |
| Neo4j | `CALLS` relationships | **none** (write-only in Phase 1) |

**Chunks are not linked chunk-to-chunk today.** In Qdrant, `callees` is a string array on each point (caller-side tokens, no target `chunk_id`). In Neo4j, `graph_writer.py` writes `(Chunk)-[:CALLS]->(Symbol)` where callee `Symbol` nodes use synthetic qualified names (`{collection}::callee::{token}`), while definition symbols use `{collection}:{rel_path}::{symbol_name}` via `(Chunk)-[:DEFINES]->(Symbol)`. Those node keys do not merge, so the graph has **no traversable path** from a caller chunk to a callee definition chunk even when both exist. `find_cross_references` joins caller and definition results at **query time** in `cross_references.py`, not via stored inter-chunk edges.

Phase 4 of ADR 0002 defers replacing Qdrant scroll passes with Cypher for multi-hop cross-project queries but does **not** specify:

- Neo4j-backed call-site lookup for `find_cross_references`
- Stopping dual-write of `callees` into Qdrant when the graph is enabled
- Payload/index cleanup or re-index policy for collections that already carry `callees`

### Why change

- **Duplicate storage:** Every call token is indexed twice when `GRAPH_ENABLED=true` — once in Qdrant payloads and once in Neo4j.
- **Wrong tool for the job:** Call-site lookup is an exact structural edge query; Neo4j `CALLS` edges are the natural representation. Qdrant keyword scroll works but adds payload size, index maintenance, and couples graph retrieval to a vector store.
- **Graph unlock:** Multi-hop questions (“who calls X that also HTTP-calls Y?”) require traversing `CALLS` in Neo4j; keeping the authoritative lookup in Qdrant prevents a single query engine for relationship-centric xref paths.
- **ADR 0002 alignment:** Phase 3–4 assume Neo4j is the relationship store; call-site lookup should follow before broad Phase 4 scroll replacement.

### Constraints

- Default deployment remains **Qdrant-only** (`GRAPH_ENABLED=false`); call-site lookup must keep working without Neo4j.
- No LLM involvement in call resolution.
- `find_cross_references` tool contract (`member`, `receiver`, `match_type: call_site`) stays backward compatible.
- Pre-release: prefer the target architecture; no long-term requirement to keep duplicate `callees` when graph is enabled.

## Decision

When the graph is enabled for a collection, **Neo4j becomes the authoritative store for call-site lookup**; Qdrant stops carrying `callees` for those collections. When the graph is disabled, Qdrant `callees` remain the sole call-site index (status quo).

Deliver in **four phases**. Phases 1–3 cover Neo4j caller query, Qdrant payload retirement, and index cleanup. Phase 4 adds optional resolved chunk-to-chunk edges for graph traversal (does not block Phases 1–3).

### Call graph model

Call relationships use a **symbol-centric graph with optional resolved chunk links** — not chunk-to-chunk only.

| Layer | Relationship | Required? | Purpose |
|-------|--------------|-----------|---------|
| **Syntactic** | `(Chunk)-[:CALLS {call_token}]->(Symbol)` | Yes | Every call expression at index time; powers exact callee lookup (`member` / `receiver` tokens); includes unresolved externals (stdlib, third-party) as stub symbols |
| **Structural** | `(Chunk)-[:DEFINES]->(Symbol)` | Yes (existing) | Where symbols are defined; unchanged from ADR 0002 |
| **Resolved** | `(Chunk)-[:CALLS_RESOLVED]->(Chunk)` | No (Phase 4) | Best-effort link from caller chunk to callee **definition chunk** when import + symbol heuristics resolve a **unique** target |

```text
                    ┌── CALLS {call_token: "repo.findById"} ──▶ Symbol (stub or unified)
CallerChunk ────────┤
                    └── CALLS_RESOLVED ──▶ CalleeChunk   (Phase 4; only when unambiguous)

CalleeChunk ──DEFINES──▶ Symbol   (same Symbol node when unified with CALLS target)
```

**Symbol unification (Phase 1):** Every `CALLS` edge stores the raw extractor token on the relationship as `call_token` (e.g. `isEnabled`, `featureManagmentService.isEnabled`). Callee lookup matches `call_token` (not synthetic qualified names alone). When a call token can be linked to exactly one `DEFINES` symbol in scope, `MERGE` the `CALLS` target to that symbol’s `qualified_name` instead of a `{collection}::callee::{token}` stub. Unresolved or ambiguous calls keep stub symbols so **100% of syntactic calls remain indexed**.

**Do not replace** `CALLS→Symbol` with chunk-to-chunk only: resolution requires import/type analysis, many calls never resolve to an in-repo chunk, bare method names are ambiguous, and large symbols may span multiple chunks with the same `symbol_name`. Chunk-to-chunk edges are an **enrichment layer**, not the canonical call record.

Chunk-to-chunk traversal for multi-hop call chains may use either:

- two hops: `CallerChunk-[:CALLS]->Symbol<-[:DEFINES]-CalleeChunk` (after unification), or
- one hop: `CallerChunk-[:CALLS_RESOLVED]->CalleeChunk` (Phase 4, when materialized)

### In scope

- Call graph schema: `call_token` on `CALLS`; symbol unification with `DEFINES`; optional `CALLS_RESOLVED` (Phase 4)
- `Neo4jStorage.find_callers` (Cypher) matching current `member` / `receiver` semantics via `call_token`
- `find_cross_references` Path D routing: Neo4j when graph enabled for target collection(s), else Qdrant scroll
- Conditional omission of `callees` from Qdrant upsert payload when graph enabled
- Index/schema: index or constraint supporting `CALLS.call_token` and `Symbol.name` lookup; full graph re-index when graph writer shape changes (pre-release: no `GRAPH_SCHEMA_VERSION` migration env)
- Tests: parity between Qdrant and Neo4j caller lookup on shared fixtures; graph-disabled regression; unified-symbol traversal fixture (Phase 1); optional `CALLS_RESOLVED` fixture (Phase 4)

### Out of scope

- Removing `callees` extraction from `chunker` (still needed for graph writer and Qdrant-only mode)
- LLM-based call graph inference or type-aware resolution beyond import + same-collection symbol heuristics
- Replacing import/definition/HTTP xref paths (Phase 4 of ADR 0002 remains separate)
- `expand_search_context` MCP tool (ADR 0002 Phase 3) — may consume unified `CALLS` / `CALLS_RESOLVED` once shipped

### Default behavior and configuration

| Mode | Call-site lookup | Qdrant `callees` payload |
|------|------------------|--------------------------|
| `GRAPH_ENABLED=false` (default) | Qdrant keyword scroll | written + indexed |
| `GRAPH_ENABLED=true`, collection graph-indexed | Neo4j `CALLS` Cypher | omitted after Phase 2 |
| `GRAPH_ENABLED=true`, collection not re-indexed with graph | Qdrant fallback with warning | present until re-index |

No new env vars required beyond existing `GRAPH_*` / `NEO4J_*` from ADR 0002. Optional future `CALL_SITE_ENGINE=qdrant|neo4j|auto` deferred — `auto` = Neo4j when enabled and collection has graph metadata, else Qdrant.

### Phased delivery

**Phase 1 — Symbol-unified CALLS + Neo4j caller query + dual-read routing**

- **Schema (full graph re-index after pull when graph writer changes):**
  - Persist `call_token` on every `(Chunk)-[:CALLS]->(Symbol)` edge (raw value from `chunk.callees`)
  - Stop relying on `{collection}::callee::{token}` qualified names as the sole lookup key; retain stubs only for unresolved calls
  - When a token uniquely matches one in-scope `DEFINES` symbol (same collection; heuristic: exact `Symbol.name` or qualified import match — lock rules at implementation), `MERGE` the `CALLS` target to that symbol node
- Add `Neo4jStorage.find_callers(method, receiver, collections, limit)` returning the same shape as `QdrantStorage.find_callers_in_collections`
- Cypher sketch (caller lookup by syntactic token):

```cypher
MATCH (col:Collection)<-[:IN_COLLECTION]-(f:File)<-[:IN_FILE]-(ch:Chunk)-[r:CALLS]->(s:Symbol)
WHERE col.name IN $collections
  AND r.call_token IN $tokens
RETURN ch.chunk_id, f.rel_path, ch.start_line, ch.end_line, col.name AS collection
LIMIT $limit
```

  (`$tokens` = `[$method]` or `[$qualified_token]` where `$qualified_token` = `receiver.method` when `receiver` is set.)

- Cypher sketch (callee definition chunks via unified symbol — for xref linking / future graph expansion):

```cypher
MATCH (caller:Chunk)-[:CALLS {call_token: $token}]->(sym:Symbol)<-[:DEFINES]-(callee:Chunk)
WHERE caller.collection = $collection
RETURN DISTINCT callee.chunk_id, callee
```

- Wire `cross_references.py` Path D through `context` to prefer Neo4j when `graph_enabled` and target collections are graph-ready
- Keep writing `callees` to Qdrant (no payload change)
- Parity tests on Java/Spring inherited-field fixtures already in `test_cross_references.py`

**Phase 2 — Stop dual-write to Qdrant**

- In `qdrant.py` upsert: skip `callees` in payload when `GRAPH_ENABLED=true` for that collection
- Collection metadata flag `graph_call_sites: true` (or reuse `graph_enabled` from ADR 0002 Phase 2 metadata)
- `find_cross_references` uses Neo4j only for graph-enabled collections; mixed batch queries use per-collection engine
- Document forced re-index when toggling graph on existing collections (already required by ADR 0002)

**Phase 3 — Retire Qdrant callees index (graph-enabled deployments only)**

- Stop creating keyword index on `callees` when graph is the deployment default
- Migration script or re-index to strip `callees` from existing Qdrant points for graph-indexed collections
- Update `ARCHITECTURE.md`, `README.md`, and `PAYLOAD_INDEXES` docs
- ADR 0002 Phase 4 cross-project Cypher paths may assume Neo4j owns `CALLS` traversal

**Phase 4 — Optional resolved chunk-to-chunk edges (best-effort)**

- After Phase 1 symbol unification, add index-time (or post-pass) materialization of `(CallerChunk)-[:CALLS_RESOLVED]->(CalleeChunk)` when resolution yields exactly one definition chunk
- Resolution scope (initial): same-collection import headers + `DEFINES` symbol name match; defer cross-collection and type-aware resolution
- Do **not** remove syntactic `(Chunk)-[:CALLS {call_token}]->(Symbol)` edges — externals and ambiguous calls remain symbol-linked only
- Multiple candidate callee chunks → no `CALLS_RESOLVED` edge (record zero or omit; no guessing)
- Split symbols (multiple chunks, same `symbol_name`): prefer the chunk whose `DEFINES` symbol matches; if still ambiguous, skip `CALLS_RESOLVED`
- Enables single-hop call-chain Cypher for [ADR 0002](0002-graphrag-neo4j-qdrant.md) Phase 3 `expand_search_context` and [ADR 0009](0009-multi-hop-retrieval-strategies.md) graph-backed hops without replacing symbol edges

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Symbol-centric CALLS + optional CALLS_RESOLVED (chosen)** | Complete syntactic coverage; traversable unified symbols; fast chunk hops when resolved; Qdrant retirement path | Schema bump; two edge types to maintain; resolution heuristics need tests |
| **Neo4j-primary when graph enabled (Qdrant migration only)** | Smaller payloads; xref via Cypher | Does not fix broken stub/defines split; weak multi-hop traversal |
| **Chunk-to-chunk CALLS only** | One-hop traversal | Incomplete graph; heavy resolution; ambiguous/unresolved calls lost |
| **Symbol-only — no CALLS_RESOLVED** | Simpler schema | Two-hop traversal always; no materialized call chain for expansion |
| **Status quo — dual-write indefinitely (ADR 0002 today)** | Qdrant-only fallback always warm; no migration | Duplicate storage; non-traversable callee stubs |
| **Neo4j-only — drop Qdrant callees entirely** | Simplest long-term | Breaks default Qdrant-only deployment unless expensive fallback (re-parse on query) |
| **Qdrant-only — never use Neo4j for call sites** | No graph dependency for xref | Wastes Neo4j `CALLS` edges; blocks multi-hop call-chain queries in graph |
| **On-query tree-sitter re-parse fallback** | No stored callees | Too slow for cross-collection scroll; duplicates index-time work |

## Consequences

### Positive

- One query engine for call-site and multi-hop call-chain traversal when graph is enabled
- Unified `CALLS`→`Symbol`←`DEFINES` paths make callee definition reachable in graph without query-time joining
- Optional `CALLS_RESOLVED` gives single-hop call chains for GraphRAG expansion without dropping syntactic edges
- Reduced Qdrant payload size and one fewer keyword index for graph deployments
- Clear ownership: structural edges in Neo4j, semantic vectors in Qdrant
- Unblocks ADR 0002 Phase 3–4 Cypher backends and ADR 0009 graph-backed multi-hop hops

### Negative / trade-offs

- Mixed-mode queries (some collections graph-enabled, some not) need per-collection routing
- Collections indexed before graph enablement need re-index to populate Neo4j `CALLS` before Qdrant `callees` can be dropped
- Cypher caller lookup must match Qdrant token semantics exactly (`method` vs `receiver.method`) — regression-sensitive
- Graph writer shape changes require full graph re-index (same as Qdrant re-index on embed model change); no versioned migration env in pre-release
- `CALLS_RESOLVED` coverage will be partial by design; clients must not assume every `CALLS` has a resolved chunk edge
- Slightly higher latency for Neo4j round-trip vs local Qdrant scroll on tiny collections (acceptable for structural queries)

### Neutral / follow-ups

- ADR 0002 Phase 2 `graph_node_ids` linking remains independent
- Consider merging Phase 1 of this ADR with ADR 0002 Phase 3 `expand_search_context` PR planning — shared Neo4j read helpers
- Evaluate whether `HTTP_CALLS` and `IMPORTS` should follow the same “Neo4j-primary when enabled” pattern in a later ADR

### Downstream work

- [0002](0002-graphrag-neo4j-qdrant.md) Phase 4 — Neo4j-backed cross-project path queries
- [0009](0009-multi-hop-retrieval-strategies.md) — graph-backed hops for call-chain questions

## Implementation notes

### Affected paths

- `mcp_server/src/codebase_indexer/indexer/graph_writer.py` — `call_token` on `CALLS`; symbol unification; Phase 4 `CALLS_RESOLVED` writer
- `mcp_server/src/codebase_indexer/storage/neo4j.py` — `find_callers` query; schema indexes for `call_token`
- `mcp_server/src/codebase_indexer/storage/qdrant.py` — conditional `callees` payload; optional index removal
- `mcp_server/src/codebase_indexer/tools/cross_references.py` — Path D engine routing
- `mcp_server/src/codebase_indexer/context.py` — expose Neo4j to xref tool when enabled
- `mcp_server/src/codebase_indexer/indexer/pipeline.py` — metadata flag for graph call sites
- `mcp_server/tests/test_cross_references.py`, `test_graph_writer.py`, `test_neo4j_storage.py` — parity, unification, and routing tests
- `docs/ARCHITECTURE.md`, `README.md`, `.env.example`

### Symbol matching and graph invariants

1. **`call_token`** on `CALLS` always equals the raw extractor output (same strings as Qdrant `callees` and `chunker._extract_callees`).
2. **Stub symbols** (`kind: callee`, unresolved) use `{collection}::callee::{call_token}` as `qualified_name` until/unless unified.
3. **Unified symbols** share one node between `CALLS` and `DEFINES` when resolution is unique; `call_token` remains on the edge for exact caller lookup.
4. **`CALLS_RESOLVED`** is derived — never the only record of a call; safe to omit when ambiguous.
5. Phase 1 Cypher filters on `r.call_token` must return the same caller chunks as Qdrant `MatchValue(value=token)`.

### Rollout

- Phase 1: opt-in behavior improvement when `GRAPH_ENABLED=true`; no payload change
- Phase 2–3: opt-in; require graph re-index before dropping Qdrant `callees`

### Data migration

- **Yes** — when enabling graph on existing collections, full re-index required (ADR 0002 invariant)
- Phase 3 optional strip of legacy `callees` from Qdrant via `force=True` re-index after Neo4j parity verified

## Validation

| Phase | Checks |
|-------|--------|
| 1 | Parity test: same `member`/`receiver` inputs → identical caller chunk set from Qdrant vs Neo4j; `call_token` on every `CALLS` edge; unified-symbol fixture traverses `CALLS→Symbol←DEFINES`; inherited Spring `@Autowired` field cases pass |
| 2 | Upsert payload omits `callees` when graph enabled; xref still resolves via Neo4j; Qdrant-only collection unchanged |
| 3 | No `callees` keyword index created when configured for graph-default; scroll filter unused for graph collections |
| 4 | `CALLS_RESOLVED` present only for unambiguous same-collection fixtures; syntactic `CALLS` edges retained for unresolved externals; no false single-target edges on ambiguous bare method names |

### Success criteria

1. With `GRAPH_ENABLED=false`, zero regression in call-site xref results and index payload shape
2. With graph enabled and collection re-indexed, `find_cross_references(..., member=...)` returns call sites without Qdrant `callees` filter
3. Neo4j and Qdrant paths agree on fixture parity suite before Phase 2 merges
4. After Phase 1 re-index, a Cypher path from caller chunk to callee definition chunk exists via unified `Symbol` for at least one fixture call chain
5. Documented re-index when enabling graph or after graph writer changes (no schema-version env var pre-1.0)
6. Phase 4 (optional): `CALLS_RESOLVED` enables single-hop call-chain query on unambiguous fixtures without removing stub `CALLS→Symbol` edges
