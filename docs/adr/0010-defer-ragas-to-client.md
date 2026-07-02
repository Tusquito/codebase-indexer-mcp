# 0010. Defer Ragas pipeline evaluation to MCP clients

- **Status:** Accepted
- **Date:** 2026-07-02
- **Deciders:** Maintainers
- **Related:** [Evaluating Pipeline Output Quality](https://qdrant.tech/documentation/improve-search/pipeline-output-quality/), [Measuring Retrieval Relevance](https://qdrant.tech/documentation/improve-search/retrieval-relevance/), [ADR 0005](0005-mcp-retrieval-connector.md), [ADR 0007](0007-ranx-retrieval-evaluation.md)

## Context

Qdrant’s [Evaluating Pipeline Output Quality](https://qdrant.tech/documentation/improve-search/pipeline-output-quality/) tutorial scores full RAG pipelines with **Ragas**: an LLM judge rates `(question, retrieved_context, answer)` triples on:

- **faithfulness** — answer supported by retrieved context
- **answer_relevancy** — answer addresses the question
- **context_precision** — retrieved chunks relevant to a reference answer

The tutorial’s key diagnostic is a **2×2 pairing** with retrieval metrics ([Measuring Retrieval Relevance](https://qdrant.tech/documentation/improve-search/retrieval-relevance/)):

| Recall@10 | Faithfulness | Diagnosis |
|-----------|--------------|-----------|
| High | High | Ship |
| High | Low | Generator / prompt problem |
| Low | Low | Fix retrieval first |
| Low | High | Incomplete labels or non-committal answers |

Our MCP server is **retrieval-only** ([ADR 0005](0005-mcp-retrieval-connector.md), [ADR 0012](0012-retrieval-only-rag-split.md)): it returns chunks and metadata; the connected client (Cursor, Claude, Copilot) runs the generator and owns prompts, models, and temperature. Running Ragas **inside** the indexer would require:

- LLM API keys in the MCP container
- A canonical “grounding prompt” that may not match each client’s behavior
- Scoring answers the server never produced

The Ragas tutorial’s **non-RAG note** applies: when retrieval feeds an agent or tool surface rather than a fixed generator, swap metrics to match the consumer.

## Decision

We will **not** implement Ragas or in-server end-to-end RAG evaluation. We adopt the Improve Search evaluation split as follows:

### In-repo (MCP / benchmarks)

- **Retrieval relevance only** — ranx golden-set harness ([ADR 0007](0007-ranx-retrieval-evaluation.md)): `recall@k`, `MRR`, `NDCG@k`
- **Latency** — existing `benchmarks/bench.py` p50/p95
- **Optional ANN recall** — operational Qdrant UI check ([ADR 0007](0007-ranx-retrieval-evaluation.md))

### Client / integrator responsibility

- **Pipeline output quality** — Ragas (or equivalent) runs in the client’s CI or eval notebook where the actual generator and judge LLM live
- Integrators pair their Ragas `faithfulness` / `context_precision` scores with our published `recall@10` baseline from the same golden `query_id` set

### Shared golden set contract

Export golden queries from [ADR 0007](0007-ranx-retrieval-evaluation.md) with:

- `query_id`, `query_text`, `collection`, `labels` (chunk_id → relevance)
- Optional `ground_truth` reference answer for client-side `context_precision`

Clients map MCP tool output to Ragas `retrieved_contexts` (chunk `content` fields) without server changes.

### What we document for integrators

1. Run `eval_retrieval.py` after indexer changes → baseline `recall@10`
2. Run client RAG loop on same golden set → Ragas scores
3. Apply the tutorial’s 2×2 table to attribute regressions
4. Do **not** use the same model as both generator and judge (tutorial pitfall)

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Retrieval in-repo; Ragas on client (chosen)** | Matches [ADR 0005](0005-mcp-retrieval-connector.md); clean separation | No single-command “full pipeline score” in repo |
| **In-server Ragas eval tool** | One-stop CI | LLM keys; unrepresentative prompt; duplicates client |
| **Ragas context_precision only in server** | Retrieval-ish signal | Still needs judge LLM and reference answers |
| **Skip pipeline eval entirely** | Zero work | Integrators lack guidance |
| **DeepEval / custom judge instead of Ragas** | Team preference | Same boundary issue — belongs with generator |

## Consequences

### Positive

- Preserves self-hosted, no-LLM-key default for the indexer
- Applies Qdrant’s retrieval vs generation attribution model correctly
- Golden set reusable for both ranx (server) and Ragas (client) on identical `query_id`s
- Avoids false confidence from a server prompt that clients do not use

### Negative / trade-offs

- No turnkey `faithfulness` gate in indexer CI
- Integrators must assemble Ragas themselves
- `context_precision` requires optional `ground_truth` curation in golden set

### Neutral / follow-ups

- ~~Example notebook `docs/examples/client-ragas-eval.ipynb` (optional, not MCP runtime)~~ → deferred; export script + DEPLOYMENT guide instead
- ~~Export script: golden set → JSON for Ragas `EvaluationDataset`~~ → `benchmarks/export_ragas_dataset.py`
- If a first-party Cursor eval harness appears, link from README

## Implementation notes

### Affected paths

- [`docs/DEPLOYMENT.md`](../DEPLOYMENT.md#pipeline-output-quality-client-side-ragas) — 2×2 diagnostic + integrator workflow
- [`mcp_server/benchmarks/fixtures/golden_queries.jsonl`](../mcp_server/benchmarks/fixtures/golden_queries.jsonl) — optional `ground_truth` on six queries
- [`mcp_server/benchmarks/export_ragas_dataset.py`](../mcp_server/benchmarks/export_ragas_dataset.py) — JSON export for client Ragas loops
- No changes to `main.py` tool surface

Phase 1 delivered:

- Golden set contract: `query_id`, `query_text`, `collection`, `labels`/`aliases`, optional `ground_truth`, `tags`
- Export CLI for integrators; retrieval harness unchanged (no LLM keys in CI)

### Rollout

Documentation only; default unchanged.

### Re-index

**No**.

## Validation

- Documented example: same `query_id` produces ranx Run from MCP search and Ragas sample from client loop
- Maintainer dry-run: toggle chunk size → `recall@10` drops → client faithfulness drops (low/low diagnosis)

Success criteria:

- Zero LLM env vars required for MCP server CI retrieval eval
- Integrator doc references both Qdrant Improve Search tutorials with clear ownership split

## Measured outcomes (2026-07-02)

Phase 1 is documentation and export only — no Ragas scores in-repo. Retrieval baseline after ADR 0009 multi-hop queries (26 total):

| Slice | recall@10 (hybrid) | Notes |
|-------|-------------------|-------|
| Overall | 0.66 | v3 golden set (`golden_set_version`: v3-multi-hop) |
| multi_hop | 0.50 | Single-pass search; expect client 2-hop scripts to beat this |
| symbol | 0.72 | Highest slice |

Integrators should pair these `query_id` metrics with client-side Ragas faithfulness using the [2×2 table](#decision) above.
