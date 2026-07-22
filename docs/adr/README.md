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
2. Use the next available four-digit number (see index below — currently **0035**)
3. Fill in all sections; leave **Status** as `Proposed` until reviewed
4. Add a row to the index table below
5. Link the ADR from related docs (e.g. [`ARCHITECTURE.md`](../ARCHITECTURE.md)) when relevant
6. Record implementation progress in [`IMPLEMENTATION_TRACKER.md`](IMPLEMENTATION_TRACKER.md) — do not use ADR bodies as a task log

## Implementation tracker

[`IMPLEMENTATION_TRACKER.md`](IMPLEMENTATION_TRACKER.md) tracks **phases, choices, and delivery status** without editing ADR decision text. The invoker applies **Tracker append** blocks from pipeline steps to update it. User-facing shipped changes go in [`CHANGELOG.md`](../../CHANGELOG.md).

Per [ADR 0019](0019-yaml-structured-adr-tracker.md), structured tracker data lives in versioned YAML under [`tracker/`](tracker/): `schema.yaml` (field contract), `tracker/phases/` (one snapshot per ADR phase), and `tracker/events/` (append-only pipeline events). **The YAML is the source of truth**; the body of `IMPLEMENTATION_TRACKER.md` is a **generated artifact**. `scripts/render_adr_tracker.py` validates the YAML and regenerates the marker-delimited blocks (summary, active, phase-logs, open-decisions) between `<!-- BEGIN/END GENERATED:* -->` markers, preserving the manual preamble/postamble.

**Do not hand-edit inside the generated markers.** To update the tracker, edit the YAML under `tracker/phases/` + `tracker/events/` and run `python scripts/render_adr_tracker.py`. CI runs `python scripts/render_adr_tracker.py --check` as a **blocking** step, so any drift between the YAML and the rendered markdown fails the build.

**Cutover complete (Phase 3).** The ADR agent pipeline now emits YAML directly: [`adr-tracker`](../../.cursor/agents/adr-tracker.md) writes an append-only `tracker/events/*.yaml`, upserts the `tracker/phases/*.yaml` snapshot, and runs the render script — there is no legacy markdown string-surgery path. Phase 2 (historical migration) is complete: all prior tracker content was migrated to `tracker/`, and the one-time `scripts/migrate_tracker_to_yaml.py` helper is archived under [`scripts/archive/`](../../scripts/archive/migrate_tracker_to_yaml.py).

## ADR pipeline agents

Step agents (`adr-prioritizer`, `adr-orchestrator`, `adr-finisher`, etc.) live in **[`.cursor/agents/`](../../.cursor/agents/)** at **project level only** — versioned with this repository. Do **not** copy them to `~/.cursor/agents/`; the orchestrator and Tasks resolve definitions from `.cursor/agents/<name>.md` in the workspace.

**Pre-release policy:** While the app is in active development, ADR agents do **not** treat backward compatibility as a constraint unless an ADR explicitly requires it. See [`.cursor/agents/project-phase.md`](../../.cursor/agents/project-phase.md). **Docker integration is mandatory for every ADR phase** before code review.

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
| [0002](0002-graphrag-neo4j-qdrant.md) | Add optional GraphRAG with Neo4j and Qdrant | Accepted (phases 1–3; phase 4 deferred) | 2026-07-02 |
| [0003](0003-hybrid-search-rrf-default.md) | Default hybrid search with prefetch and RRF fusion | Accepted | 2026-07-02 |
| [0004](0004-collection-per-project-isolation.md) | Collection-per-project isolation over payload multitenancy | Accepted | 2026-07-02 |
| [0005](0005-mcp-retrieval-connector.md) | MCP as external RAG retrieval connector | Accepted | 2026-07-02 |
| [0006](0006-explicit-fastembed-pipeline.md) | Explicit FastEmbed pipeline over qdrant-client convenience API | Accepted | 2026-07-02 |
| [0007](0007-ranx-retrieval-evaluation.md) | Golden-set retrieval evaluation with ranx | Accepted | 2026-07-02 |
| [0008](0008-optional-colbert-reranking.md) | Optional ColBERT late-interaction reranking | Accepted | 2026-07-02 |
| [0009](0009-multi-hop-retrieval-strategies.md) | Multi-hop code retrieval strategies | Accepted (phase 1; phase 2 merged) | 2026-07-02 |
| [0010](0010-defer-ragas-to-client.md) | Defer Ragas pipeline evaluation to MCP clients | Accepted | 2026-07-02 |
| [0011](0011-ollama-only-dense-embedding.md) | Ollama-only dense embedding | Superseded (→ [0025](0025-huggingface-tei-dense-embedding.md)) | 2026-07-02 |
| [0012](0012-retrieval-only-rag-split.md) | Keep MCP as retrieval-only RAG layer | Accepted | 2026-07-02 |
| [0013](0013-external-agent-knowledge-base.md) | Expose Qdrant retrieval via MCP for external agent orchestrators | Accepted | 2026-07-02 |
| [0014](0014-vector-discovery-and-ops-automation.md) | Adopt Qdrant vector discovery APIs and optional n8n ops hooks | Accepted (phase 1; phase 2 — outlier / diversity helper) | 2026-07-02 |
| [0015](0015-colbert-http-sidecar.md) | ColBERT HTTP sidecar | Accepted | 2026-07-03 |
| [0016](0016-qwen3-embedding-default-dense-model.md) | Adopt Qwen3-Embedding-4B as default Ollama dense model | Superseded (default policy → [0021](0021-revert-jina-production-default-retire-qwen3.md)) | 2026-07-03 |
| [0017](0017-model-tokenizer-tei-dense-truncation.md) | Model-accurate tokenizer for TEI dense truncation | Accepted (phase 1 — loader + TEI backend) | 2026-07-03 |
| [0018](0018-telemetry-observability-otel-prometheus.md) | Adopt OpenTelemetry instrumentation with Prometheus metrics and optional OTLP export | Accepted (phase 1 — Application Prometheus metrics (MCP + ColBERT worker)) | 2026-07-03 |
| [0019](0019-yaml-structured-adr-tracker.md) | Adopt YAML structured events for ADR implementation tracking | Accepted (phase 3) | 2026-07-03 |
| [0020](0020-qwen3-code-finetune-jina-quality-gate.md) | Fine-tune Qwen3 for code retrieval with Jina quality gate | Accepted (phase 1 only; phases 2–4 cancelled — gate failed) | 2026-07-03 |
| [0021](0021-revert-jina-production-default-retire-qwen3.md) | Revert default dense embedder to Jina code; retire Qwen3 as production default | Accepted | 2026-07-04 |
| [0022](0022-gpu-default-cpu-fallback.md) | GPU-default acceleration; CPU only when explicit | Accepted | 2026-07-04 |
| [0023](0023-neo4j-primary-call-site-lookup.md) | Move call-site lookup from Qdrant callees to Neo4j CALLS | Accepted (phase 1; phase 2 — Stop dual-write to Qdrant) | 2026-07-04 |
| [0024](0024-resource-aware-stack-tuner.md) | Add resource-aware stack tuner for RSS allocation and performance tuning | Accepted | 2026-07-04 |
| [0025](0025-huggingface-tei-dense-embedding.md) | Adopt HuggingFace TEI sidecar for dense embedding (hard replace of Ollama dense) | Accepted (all phases complete) | 2026-07-04 |
| [0026](0026-full-stack-embedding-quality-benchmark.md) | Full-stack embedding model quality benchmark and selection framework | Accepted (phases 1–3; phases 4–5 open) | 2026-07-08 |
| [0027](0027-client-side-search-intent-routing.md) | Client-side search intent routing before retrieval | Proposed | 2026-07-10 |
| [0028](0028-apple-silicon-arm64-cpu-deployment.md) | Apple Silicon arm64 CPU-first deployment profile | Accepted | 2026-07-12 |
| [0029](0029-macos-host-native-tei-metal-acceleration.md) | macOS host-native TEI with Metal for dense embedding acceleration | Accepted | 2026-07-12 |
| [0030](0030-migrate-mcp-server-to-dotnet10.md) | Migrate MCP server runtime from Python to C# .NET 10 | Accepted | 2026-07-12 |
| [0031](0031-mcp-liveness-vs-readiness.md) | Split MCP liveness from dependency readiness | Accepted (phase 1 shipped; phase 2 open) | 2026-07-21 |
| [0032](0032-replace-magic-strings-with-enums.md) | Replace closed-set magic strings with domain enums | Accepted | 2026-07-21 |
| [0033](0033-adopt-result-pattern.md) | Adopt Result pattern for expected failures | Accepted | 2026-07-21 |
| [0034](0034-migrate-unit-tests-to-tunit.md) | Adopt TUnit as the full .NET test stack | Accepted | 2026-07-22 |

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
| Pre-search intent / tool routing | Yes | [0027](0027-client-side-search-intent-routing.md) — client-side; optional heuristic MCP hint |
| Recommendation API (like / not like) | Yes | [0014](0014-vector-discovery-and-ops-automation.md) — Phase 1: `recommend_code` |
| Movie / song / image search notebooks | No | Wrong modality (recommendations, audio, vision) |
| Extractive QA | No | In-server answer generation conflicts with [0005](0005-mcp-retrieval-connector.md) |

## References

- [Documenting Architecture Decisions](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions) — Michael Nygard
- [MADR](https://adr.github.io/madr/) — Markdown Any Decision Records
