# 0027. Client-side search intent routing before retrieval

- **Status:** Proposed
- **Date:** 2026-07-10
- **Deciders:** Maintainers
- **Related:** [ADR 0005](0005-mcp-retrieval-connector.md), [ADR 0007](0007-ranx-retrieval-evaluation.md), [ADR 0009](0009-multi-hop-retrieval-strategies.md), [ADR 0012](0012-retrieval-only-rag-split.md), [Measuring Retrieval Relevance](https://qdrant.tech/documentation/improve-search/retrieval-relevance/)

## Context

Retrieval quality on this stack depends on **which tool and strategy run first**, not only on embedding model or hybrid RRF tuning. The golden set in [ADR 0007](0007-ranx-retrieval-evaluation.md) already encodes query modes as tags — `symbol`, `conceptual`, `config`, `cross_file`, `multi_hop` — because each mode favors a different retrieval path:

| Query mode | Typical user question | Better first move |
|------------|----------------------|-------------------|
| `symbol` | “Where is `Embedder` defined?” | `search_symbols` → `get_chunk` |
| `config` | “What env var controls hybrid search?” | `search_codebase` (often config/docs paths) |
| `cross_file` | “Who registers the xref MCP tool?” | `find_cross_references` or `search_symbols` |
| `conceptual` | “How does hybrid prefetch + RRF work?” | `search_codebase` with truncated content |
| `multi_hop` | “How does cron → MCP → pipeline connect?” | Multi-hop loop (2–4 searches + client RRF fuse) |

Today this routing is **implicit**: documented in `skill/codebase-indexer/SKILL.md`, `docs/SEARCH_BEHAVIOR.md`, and MCP `_INSTRUCTIONS` in `main.py`. Clients that skip the tool ladder and call `search_codebase` with the raw user question pay unnecessary embedding cost and often miss evidence that structural tools or multi-hop fusion would reach.

[ADR 0009](0009-multi-hop-retrieval-strategies.md) covers **post-search** query decomposition (sub-questions after hop 1) and defers **HyDE** (LLM-generated hypothetical documents embedded before search). Neither ADR addresses **pre-search intent classification** — deciding tool, parameters, and multi-hop likelihood **before** the first embed-heavy call.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Infrastructure / ANN recall | no | Unchanged |
| Ranked retrieval relevance (layer 2) | yes | Route accuracy vs golden tags; optional lift on mis-routed baselines |
| Pipeline output / Ragas | no | [ADR 0010](0010-defer-ragas-to-client.md) — client-side |
| Business KPIs | no | Out of scope |

### Hard constraints

- [ADR 0005](0005-mcp-retrieval-connector.md) / [ADR 0012](0012-retrieval-only-rag-split.md): **no LLM API keys in the MCP server**; LLM-based intent resolution stays in the client.
- Pre-search routing must compose with existing tools — not replace hybrid search, ColBERT rerank, or GraphRAG ([ADR 0002](0002-graphrag-neo4j-qdrant.md)).
- Default deployment must remain usable when clients ignore routing guidance (server behavior unchanged).

### Why now

- ADR 0009 phases 1–2 merged (multi-hop docs + `eval_multihop.py`); the remaining quality gap for many queries is **wrong first tool**, not missing hop-2 fusion.
- Golden-set tags provide a ready-made routing label set for offline routing-accuracy evaluation without new annotation.
- HyDE remains deferred ([ADR 0009](0009-multi-hop-retrieval-strategies.md)); lighter-weight routing delivers most of the orchestration benefit at lower latency and failure risk.

## Decision

We will add **client-side search intent routing** as a documented, evaluable playbook — and optionally a **deterministic server hint tool** — that runs **before** the first retrieval tool call. Routing selects tool, parameters, and whether to plan a multi-hop loop; it does **not** embed queries or synthesize answers.

### Intent schema (client output)

Clients (or skills) emit a small structured plan before calling MCP tools:

```json
{
  "intent": "locate_symbol | explain_behavior | find_config | trace_usage | cross_service | project_overview",
  "strategy": "tool_chain | single_search | multi_hop_decomposition | graph_expand",
  "first_tool": "search_symbols",
  "needs_multi_hop": false,
  "search_query": "optional rewritten query for embed tools only",
  "tool_params": {
    "top_k": 10,
    "max_content_chars": 300,
    "rerank": false
  },
  "fallback": "if first hop thin, escalate to search_codebase or decomposition"
}
```

**LLM-based resolution** (structured output in the MCP client) is the primary mechanism. **Heuristic rules** (regex, CamelCase identifiers, “callers of”, file paths in the question) may supplement or replace LLM routing for deterministic clients.

### Routing map (normative defaults)

| `intent` | `first_tool` | `needs_multi_hop` | Notes |
|----------|--------------|-------------------|-------|
| `locate_symbol` | `search_symbols` | false | User names a symbol or asks “where is X defined” |
| `trace_usage` | `find_cross_references` | false when member known | Prefer member/receiver over semantic search |
| `cross_service` | `map_service_dependencies` | often true | HTTP / microservice edges |
| `find_config` | `search_codebase` | false | Bias toward config/manifest paths when client supports `path_glob` |
| `explain_behavior` | `search_codebase` or `search_symbols` | true when question spans files | Start cheap (`search_symbols`) when a symbol anchor exists |
| `project_overview` | `get_collection_summary` | false | Zero embed; often sufficient alone |

When `needs_multi_hop=true`, clients follow [ADR 0009](0009-multi-hop-retrieval-strategies.md) decomposition + RRF fuse — routing only sets the **entry** path and hop budget, not sub-questions (those remain post-hop-1).

### In scope

- Phase 1 — **Documentation + skill playbook**: “Step 0: resolve intent” in `skill/codebase-indexer/SKILL.md`; cross-link in `docs/SEARCH_BEHAVIOR.md` and `main.py` `_INSTRUCTIONS`; intent schema + routing table above.
- Phase 2 — **Optional deterministic MCP tool** `suggest_search_strategy(query)` (name TBD): regex/heuristic router only, zero LLM, returns `{ intent, first_tool, needs_multi_hop, tool_params, hints[] }`; gated by `INTENT_ROUTING_ENABLED=true` (default off).
- Phase 3 — **Routing eval harness**: `benchmarks/eval_routing.py` (or extend `eval_retrieval.py`) — compare heuristic (and optionally logged client) routing decisions against golden-set `tags`; report per-tag accuracy and “wrong first tool” rate; non-blocking CI.

### Out of scope

- **HyDE / hypothetical document embedding** — remains deferred per [ADR 0009](0009-multi-hop-retrieval-strategies.md).
- **In-server LLM intent resolver** — violates [ADR 0005](0005-mcp-retrieval-connector.md).
- **Automatic query rewriting required for search** — optional `search_query` in client schema only; server tools accept user/client query as today.
- **Server-side multi-hop loop or hop fusion endpoint** — remains out of scope per ADR 0009.
- **Replacing** post-search decomposition — orthogonal; routing sets hop 0, decomposition handles hops 1..N.

### Default behavior and configuration

- **Default:** unchanged server behavior; routing is opt-in client/skill convention until Phase 2 tool ships.
- **Phase 2 config:** `INTENT_ROUTING_ENABLED` (default `false`) registers `suggest_search_strategy`; no effect on existing search tools.

### Phased delivery

1. **Phase 1** — Docs + skill Step 0 (no server code).
2. **Phase 2** — Heuristic `suggest_search_strategy` MCP tool + unit tests.
3. **Phase 3** — Routing accuracy benchmark vs golden tags + SEARCH_BEHAVIOR eval section.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Client intent routing + optional heuristic hint tool (chosen)** | Matches ADR 0005; uses golden tags; no model keys in server; cheap Phase 1 | Quality depends on client/skill adoption; misrouting possible |
| **Enrich ADR 0009 only (no new ADR)** | Single multi-hop doc | Conflates pre-search routing with post-search decomposition; HyDE deferral already buried in alternatives table |
| **HyDE / pre-search LLM rewrite in server** | Better semantic recall on prose queries | Extra LLM call; server keys; deferred in 0009 |
| **Always `search_codebase` first** | Simplest client | Worst token cost; misses structural tools; multi_hop slice underperforms single-pass by design |
| **ML classifier in server (no LLM)** | Deterministic; no client changes | Training data maintenance; overlaps golden tags but adds deployment complexity before heuristics prove value |

## Consequences

### Positive

- Closes the documented gap between tool ladder guidance and enforced behavior.
- Reuses golden-set tags as routing labels — aligns layer-2 eval with orchestration.
- Composes with ADR 0009 multi-hop (routing flags `needs_multi_hop` upfront) and ADR 0002 GraphRAG (`strategy: graph_expand` when `GRAPH_ENABLED=true`).
- Phase 2 hint tool helps clients that ignore long MCP instructions without putting LLM in the server.

### Negative / trade-offs

- Misrouting (e.g. `trace_usage` → `search_codebase`) can be worse than an unfocused first search — schema requires explicit `fallback`.
- Heuristic router will not match LLM routing quality on paraphrased conceptual questions.
- Another concept for maintainers and skill authors to keep in sync with tool surface changes.

### Neutral / follow-ups

- Consider promoting Phase 1 to skill default for Copilot CLI users via `skill/codebase-indexer/SKILL.md`.
- If heuristic accuracy ≥90% on `symbol` + `config` tags, Phase 2 tool may be sufficient without client LLM routing for those modes.
- Cross-link from [ADR 0009](0009-multi-hop-retrieval-strategies.md) neutral follow-ups (done in same delivery).

### Downstream work

- [0009](0009-multi-hop-retrieval-strategies.md) — entry path into decomposition loop
- [0007](0007-ranx-retrieval-evaluation.md) — routing accuracy metrics by tag
- [0002](0002-graphrag-neo4j-qdrant.md) — `graph_expand` strategy when `expand_search_context` ships

## Implementation notes

### New artifacts (by phase)

| Phase | Artifact |
|-------|----------|
| 1 | `skill/codebase-indexer/SKILL.md` — Step 0 intent block; `docs/SEARCH_BEHAVIOR.md` — “Intent routing” section |
| 2 | `mcp_server/src/codebase_indexer/tools/intent.py` (or `routing.py`); `tests/test_intent_routing.py` |
| 3 | `mcp_server/benchmarks/eval_routing.py`; fixture notes in `golden_queries.jsonl` README or SEARCH_BEHAVIOR |

### Modified artifacts

- `mcp_server/src/codebase_indexer/main.py` — register Phase 2 tool; `_INSTRUCTIONS` cross-link
- `mcp_server/src/codebase_indexer/config.py` — `INTENT_ROUTING_ENABLED` (Phase 2)
- `.github/copilot-instructions.md` — doc sync if skill ladder changes

### Rollout

- Phase 1: documentation only; default unchanged.
- Phase 2: opt-in tool registration.
- Phase 3: benchmark-only; no CI gate until routing labels stable.

### Data migration

- **No** re-index. Phase 3 may add `routing_eval_baseline.json` beside `eval_baseline.json`.

## Validation

### Phase 1

- Manual: agent following SKILL Step 0 picks `search_symbols` for three golden `symbol` queries without calling `search_codebase` first.
- Manual: multi-hop golden query sets `needs_multi_hop=true` before first search.

### Phase 2

- Unit tests: heuristic router maps fixture strings to expected `first_tool` for each tag class (symbol, config, xref phrasing, overview).
- Tool returns within 5 ms p95 (no embed, no network).

### Phase 3

- `eval_routing.py` reports accuracy vs golden `tags` (primary label = first non-`multi_hop` tag when compound).
- Side metric: run `eval_retrieval` on queries where routing matched vs mismatched first tool (optional A/B on heuristic router).

### Success criteria

1. Published intent schema and routing table in SEARCH_BEHAVIOR + skill.
2. Phase 2 heuristic ≥85% first-tool accuracy on `symbol` + `config` golden rows (deterministic fixtures).
3. Documented relationship to ADR 0009 decomposition (pre-search vs post-search) — no overlap confusion.
