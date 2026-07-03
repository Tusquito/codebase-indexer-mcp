# Architecture Decision Records (ADR)

This folder tracks significant architecture decisions for the codebase-indexer MCP server. Each ADR captures **why** a decision was made, not just what changed.

## When to write an ADR

Create a new ADR when a decision:

- Affects multiple components or deployment topology
- Is hard to reverse (data formats, external dependencies, public APIs)
- Has meaningful trade-offs that future contributors should understand
- Changes default behavior or operational assumptions

Skip ADRs for routine bug fixes, refactors with no design change, or dependency bumps that preserve behavior.

## How to add an ADR

1. Copy [`template.md`](template.md) to a new file: `NNNN-short-kebab-title.md`
2. Use the next available four-digit number (see index below — currently **0016**)
3. Fill in all sections; leave **Status** as `Proposed` until reviewed
4. Add a row to the index table below
5. Link the ADR from related docs (e.g. [`ARCHITECTURE.md`](../ARCHITECTURE.md)) when relevant
6. Record implementation progress in [`IMPLEMENTATION_TRACKER.md`](IMPLEMENTATION_TRACKER.md) — do not use ADR bodies as a task log

## Implementation tracker

[`IMPLEMENTATION_TRACKER.md`](IMPLEMENTATION_TRACKER.md) tracks **phases, choices, and delivery status** without editing ADR decision text. The invoker applies **Tracker append** blocks from pipeline steps to update it. User-facing shipped changes go in [`CHANGELOG.md`](../../CHANGELOG.md).

## ADR pipeline agents

Step agents (`adr-prioritizer`, `adr-orchestrator`, `adr-finisher`, etc.) live in **[`.cursor/agents/`](../../.cursor/agents/)** at **project level only** — versioned with this repository. Do **not** copy them to `~/.cursor/agents/`; the orchestrator and Tasks resolve definitions from `.cursor/agents/<name>.md` in the workspace.

Invoke the full pipeline with **`adr-orchestrator`**; resume a phase with `Resume from: 6` (see [`IMPLEMENTATION_TRACKER.md`](IMPLEMENTATION_TRACKER.md)).

## Status lifecycle

| Status | Meaning |
|--------|---------|
| Proposed | Under discussion; not yet adopted |
| Accepted | Decision is in effect |
| Deprecated | Superseded or no longer recommended |
| Superseded | Replaced by a newer ADR (link the successor) |

## Index

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [0001](0001-pluggable-embed-backends.md) | Introduce pluggable embedding backends | Superseded | 2026-07-02 |
| [0002](0002-graphrag-neo4j-qdrant.md) | Add optional GraphRAG with Neo4j and Qdrant | Proposed | 2026-07-02 |
| [0003](0003-hybrid-search-rrf-default.md) | Default hybrid search with prefetch and RRF fusion | Accepted | 2026-07-02 |
| [0004](0004-collection-per-project-isolation.md) | Collection-per-project isolation over payload multitenancy | Accepted | 2026-07-02 |
| [0005](0005-mcp-retrieval-connector.md) | MCP as external RAG retrieval connector | Accepted | 2026-07-02 |
| [0006](0006-explicit-fastembed-pipeline.md) | Explicit FastEmbed pipeline over qdrant-client convenience API | Accepted | 2026-07-02 |
| [0007](0007-ranx-retrieval-evaluation.md) | Golden-set retrieval evaluation with ranx | Accepted | 2026-07-02 |
| [0008](0008-optional-colbert-reranking.md) | Optional ColBERT late-interaction reranking | Accepted | 2026-07-02 |
| [0009](0009-multi-hop-retrieval-strategies.md) | Multi-hop code retrieval strategies | Accepted (phase 1; phase 2 merged) | 2026-07-02 |
| [0010](0010-defer-ragas-to-client.md) | Defer Ragas pipeline evaluation to MCP clients | Accepted | 2026-07-02 |
| [0011](0011-ollama-only-dense-embedding.md) | Ollama-only dense embedding | Accepted | 2026-07-02 |
| [0012](0012-retrieval-only-rag-split.md) | Keep MCP as retrieval-only RAG layer | Accepted | 2026-07-02 |
| [0013](0013-external-agent-knowledge-base.md) | Expose Qdrant retrieval via MCP for external agent orchestrators | Accepted | 2026-07-02 |
| [0014](0014-vector-discovery-and-ops-automation.md) | Adopt Qdrant vector discovery APIs and optional n8n ops hooks | Accepted (phase 1) | 2026-07-02 |
| [0015](0015-colbert-http-sidecar.md) | ColBERT HTTP sidecar | Accepted | 2026-07-03 |

## Qdrant Build Prototypes & Improve Search map

Cross-reference for [Build Prototypes](https://qdrant.tech/documentation/examples/) and [Improve Search](https://qdrant.tech/documentation/improve-search/) samples.

| Qdrant sample | Applicable? | ADR / note |
|---------------|-------------|------------|
| Multitenancy with LlamaIndex | Yes | [0004](0004-collection-per-project-isolation.md) |
| Cohere RAG connector | Yes | [0005](0005-mcp-retrieval-connector.md) |
| RAG chatbots (Haystack, LangChain, DSPy, Cohere, …) | Partial | [0005](0005-mcp-retrieval-connector.md) — retrieval only; generation in client |
| Hybrid Search on PDF Manuals | Yes | [0003](0003-hybrid-search-rrf-default.md) |
| GraphRAG Agent (Neo4j + Qdrant) | Yes | [0002](0002-graphrag-neo4j-qdrant.md) |
| Basic RAG / Intro to Semantic Search | Yes | [0005](0005-mcp-retrieval-connector.md), [0006](0006-explicit-fastembed-pipeline.md) |
| Build Semantic / Hybrid Search API (FastAPI) | Yes | [0005](0005-mcp-retrieval-connector.md), [0006](0006-explicit-fastembed-pipeline.md) — MCP replaces FastAPI search surface |
| Hybrid Search with Reranking (ColBERT) | Yes | [0008](0008-optional-colbert-reranking.md), [0015](0015-colbert-http-sidecar.md) (sidecar deployment) |
| Measuring Retrieval Relevance (ranx) | Yes | [0007](0007-ranx-retrieval-evaluation.md) |
| Evaluating Pipeline Output Quality (Ragas) | Partial | [0010](0010-defer-ragas-to-client.md) — client-side only; export via `export_ragas_dataset.py` |
| Query Decomposition (multi-hop) | Yes | [0009](0009-multi-hop-retrieval-strategies.md) |
| Recommendation API (like / not like) | Yes | [0014](0014-vector-discovery-and-ops-automation.md) — Phase 1: `recommend_code` |
| Movie / song / image search notebooks | No | Wrong modality (recommendations, audio, vision) |
| Extractive QA | No | In-server answer generation conflicts with [0005](0005-mcp-retrieval-connector.md) |

## References

- [Documenting Architecture Decisions](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions) — Michael Nygard
- [MADR](https://adr.github.io/madr/) — Markdown Any Decision Records
