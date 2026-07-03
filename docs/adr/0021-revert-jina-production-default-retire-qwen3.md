# 0021. Revert default dense embedder to Jina code; retire Qwen3 as production default

- **Status:** Proposed
- **Date:** 2026-07-03
- **Deciders:** Maintainers
- **Related:** [0016](0016-qwen3-embedding-default-dense-model.md) — Qwen3 default (superseded for production); [0020](0020-qwen3-code-finetune-jina-quality-gate.md) — fine-tune gate (failed path); [0007](0007-ranx-retrieval-evaluation.md) — golden-set eval; [0011](0011-ollama-only-dense-embedding.md) — Ollama-only dense; [0017](0017-model-tokenizer-ollama-dense-truncation.md) — tokenizer truncation (model-agnostic)
- **Supersedes:** [0016](0016-qwen3-embedding-default-dense-model.md) — **default model recommendation only** (Qwen3 remains an optional preset)

## Context

[ADR 0016](0016-qwen3-embedding-default-dense-model.md) changed the recommended default dense model from Nomic to **Qwen3-Embedding-4B** based primarily on **CoIR** leaderboard rank (#5 among Ollama-available code embedders) and 40K context. Phase 2 measured the switch on this repository’s golden set ([ADR 0007](0007-ranx-retrieval-evaluation.md)) and documented a **severe regression** vs the prior **Jina v2 base code** baseline:

| Variant | recall@10 | MRR | NDCG@10 | Indexed with |
|---------|-----------|-----|---------|--------------|
| **Jina** (`jinaai/jina-embeddings-v2-base-code`) | **0.660** | **0.587** | **0.539** | Jina @ 768, hybrid |
| Qwen3-4B @ 1024 MRL | 0.244 | 0.262 | 0.191 | Qwen3 @ 1024, hybrid |
| Delta | **−63.1%** | −55.2% | −64.5% | |

Phase 2 merged with an explicit **waiver** — CoIR rank was treated as sufficient for new GPU installs. That left operators who run `eval_retrieval --compare` with a default that **objectively underperforms** the model the golden set was built and tuned for.

[ADR 0020](0020-qwen3-code-finetune-jina-quality-gate.md) defined the recovery path: **LoRA fine-tune Qwen3** on golden-set pairs and **promote only if** fine-tuned recall@10 ≥ Jina (**0.660**, no waiver). Phase 1 shipped offline training tooling ([PR #15](https://github.com/Tusquito/codebase-indexer-mcp/pull/15)). Maintainer experiment confirmed:

1. **Base Qwen3 already fails the gate** by a wide margin — no fine-tune required to reject production use.
2. **Fine-tune on 26 golden queries** is high overfit risk; closing a 63 pp recall gap on identifier- and config-shaped queries is not credible without substantially more labeled data and a completed Ollama export path (ADR 0020 Phases 2–3, never merged).
3. **Per ADR 0020 policy:** when the gate fails, **stop investing in Qwen3-as-default** and treat **Jina as the production embedder** for code search on this stack.

### Why Qwen3 is a poor fit for *this* repository (not “bad model” globally)

| Factor | Jina v2 base code | Qwen3-Embedding-4B | Implication |
|--------|-------------------|---------------------|-------------|
| Repo golden recall@10 | **0.660** | 0.244 | Production default must favor measured retrieval on *this* corpus |
| `config` tag recall@10 | 0.400 | **0.000** | Qwen3 misses env/settings discovery entirely on fixture |
| `symbol` tag recall@10 | **0.722** | 0.278 | Identifier-shaped queries (`class Foo`, `def bar`) — core MCP use case — regress heavily |
| `conceptual` tag recall@10 | **0.810** | 0.190 | Natural-language + code hybrid queries fail |
| Multi-hop 2-hop RRF recall@10 | **0.333** | 0.167 | Client-side fusion also worse |
| Golden set provenance | Labels tuned under Jina/Nomic chunk + query style | CoIR-motivated switch without label realignment | Benchmark mismatch, not just “wrong hyperparams” |
| Hybrid + BM25 interaction | Strong on symbol/conceptual tags ([ADR 0007](0007-ranx-retrieval-evaluation.md) v2 findings) | Dense channel dominates misses; BM25 lift minimal (+1.3 pp dense-only → hybrid) | Qwen3 dense vectors do not complement BM25 the way Jina does here |
| CoIR / public benchmarks | Strong code model; not top-5 CoIR | #5 CoIR | **Leaderboard rank does not predict** this repo’s chunking, query phrasing, or RRF setup |
| Operational cost | ~768 dims; community Ollama port proven in repo | 4B params; 1024 MRL; full re-index; heavier GPU | Higher cost **without** retrieval benefit on golden set |
| Fine-tune viability | N/A (already wins) | 26-query corpus; maintainer GPU; no promoted checkpoint | Recovery path closed per ADR 0020 gate policy |

**Conclusion:** Qwen3 is a capable general code embedder on public benchmarks but is **not suited as the default dense encoder** for codebase-indexer-mcp given how we chunk, query, fuse hybrid search, and measure quality. **Jina v2 base code** is the evidence-backed production choice for this project until a *different* model beats `eval_baseline_jina.json` on the same harness without waiver.

### Hard constraints

1. **Ollama-only dense inference** unchanged ([ADR 0011](0011-ollama-only-dense-embedding.md)).
2. **Hybrid search** unchanged ([ADR 0003](0003-hybrid-search-rrf-default.md)).
3. **Dimension revert** — Jina @ **768** requires **full re-index** of every collection (Qwen3 1024-dim vectors incompatible).
4. **Do not delete Qwen3 registry entries** — keep as documented optional preset for operators who accept CoIR over golden-set score.

### Evaluation stack

| Layer | Decision use |
|-------|----------------|
| Golden-set ranx (layer 2) | **Primary** — Jina wins; drives revert |
| CoIR / MTEB | Informative only — insufficient to override fixture |
| ADR 0020 fine-tune gate | **Failed path** — no promotion |
| End-user Ragas | Out of scope ([ADR 0010](0010-defer-ragas-to-client.md)) |

### Why now

- ADR 0016 + 0020 embedding track is complete through “attempt recovery”; recovery did not meet gate.
- Continuing Qwen3-as-default misleads operators and wastes GPU on indexing inferior retrieval.
- Jina Ollama port (`unclemusclez/jina-embeddings-v2-base-code`) is already validated in this repo’s eval history.

## Decision

We will **revert the recommended production default** to **Jina Embeddings v2 base code** (`jinaai/jina-embeddings-v2-base-code` / Ollama `unclemusclez/jina-embeddings-v2-base-code`) at **768 dimensions**, refresh committed eval baselines to Jina metrics, and **demote Qwen3** from default documentation to an optional **experimental preset** with explicit golden-set regression warning.

[ADR 0016](0016-qwen3-embedding-default-dense-model.md) default recommendation is **superseded**. [ADR 0020](0020-qwen3-code-finetune-jina-quality-gate.md) Phases 2–4 (Ollama export, gate commit, CI job) are **cancelled** — gate failed path.

### In scope

| Area | Change |
|------|--------|
| Default env | `.env.example`, `.env.compose.integration` — Jina model ids, `DENSE_EMBED_VECTOR_SIZE=768`, `OLLAMA_EMBED_MODEL=unclemusclez/jina-embeddings-v2-base-code` |
| Benchmark defaults | `mcp_server/benchmarks/_settings.py` — Jina embed defaults |
| Eval baseline | `eval_baseline.json` — restore Jina hybrid metrics from `eval_baseline_jina.json` (or re-run live verify) |
| Docs | `README.md`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md` — Jina primary; Qwen3 moved to “Experimental / CoIR preset” with regression note |
| Compose / scripts | `scripts/run_compose_integration.py` — Jina defaults for integration generator |
| ADR index | Mark 0016 default policy superseded; 0020 phases 2–4 cancelled in tracker |
| Qwen3 preset | Comment block only — not removed from `KNOWN_EMBED_MODEL_*` or MRL passthrough ([0016](0016-qwen3-embedding-default-dense-model.md) code remains for opt-in) |

### Out of scope

- Removing Qwen3 from `config.py` registry or `OllamaDenseBackend` MRL support (opt-in still valid)
- Deleting `mcp_server/benchmarks/train/` (ADR 0020 Phase 1 tooling stays for future experiments)
- Re-labeling entire golden set for Qwen3 query style
- HuggingFace in-process Jina (violates [ADR 0011](0011-ollama-only-dense-embedding.md))
- Automatic re-index on model change ([ADR 0011](0011-ollama-only-dense-embedding.md) deferral stands)

### Default behavior and configuration

- **Default:** **breaking revert** for installs that adopted Qwen3 defaults from ADR 0016 — must pull Jina Ollama model, update env, **force re-index**.
- **Production preset (new default):**

| Variable | Value |
|----------|-------|
| `DENSE_EMBED_MODEL` | `jinaai/jina-embeddings-v2-base-code` |
| `OLLAMA_EMBED_MODEL` | `unclemusclez/jina-embeddings-v2-base-code` |
| `DENSE_EMBED_VECTOR_SIZE` | `768` |
| `MAX_DENSE_EMBED_TOKENS` | `0` (auto → 8192 from registry) |

- **Experimental preset (documented, not default):**

| Variable | Value | Warning |
|----------|-------|---------|
| `DENSE_EMBED_MODEL` | `Qwen/Qwen3-Embedding-4B` | −63% recall@10 vs Jina on repo golden set (ADR 0016) |
| `OLLAMA_EMBED_MODEL` | `qwen3-embedding:4b` | Requires GPU; full re-index @ 1024 MRL |

### Phased delivery

1. **Phase 1 — Config + docs revert** — `.env.example`, `_settings.py`, README/ARCHITECTURE/DEPLOYMENT; Qwen3 demoted to experimental block.
2. **Phase 2 — Eval baseline refresh** — re-index golden fixture with Jina; commit `eval_baseline.json`; ADR **Measured outcomes** table.
3. **Phase 3 — ADR housekeeping** — update 0016 status note (superseded default); 0020 tracker entry (phases 2–4 cancelled); CHANGELOG.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Revert default to Jina (chosen)** | Best golden-set recall; proven in repo; matches operator expectations | Breaking for Qwen3 adopters; 768-dim re-index; community Ollama port |
| **Keep Qwen3 default; document regression** | No migration | Default objectively worse; undermines ADR 0007 investment |
| **Continue ADR 0020 fine-tune track** | Might close gap eventually | 26-query corpus; gate failed at base; high maintainer cost; unproven |
| **Revert to Nomic CPU preset** | Smallest model | Worse than Jina on golden set; ADR 0007 v2 tuned away from Nomic |
| **Trust CoIR; discard golden set** | Simple narrative | Not reproducible; wrong for this product’s eval culture |
| **Remove Qwen3 code entirely** | Minimal registry | Breaks opt-in; wastes merged MRL work; no upside |

## Consequences

### Positive

- Default dense model **matches measured retrieval quality** on the repo golden set
- Operators running `eval_retrieval --compare` see baseline aligned with defaults
- Stops GPU/time spent indexing with a proven inferior encoder for this stack
- Clear policy: **fixture beats leaderboard** for default selection ([ADR 0007](0007-ranx-retrieval-evaluation.md))

### Negative / trade-offs

- **Breaking** for ADR 0016 adopters on Qwen3 @ 1024 — re-index required
- Jina **8K context** vs Qwen3 40K — long-chunk truncation returns (mitigated by [ADR 0017](0017-model-tokenizer-ollama-dense-truncation.md) accurate truncation)
- Community Ollama Jina port — not official Ollama library (already documented in [ADR 0011](0011-ollama-only-dense-embedding.md))
- ADR 0016 narrative partially invalidated — document honestly in Measured outcomes

### Neutral / follow-ups

- Qwen3 reranker as **query-time** boost only — separate ADR if pursued (does not fix index-time dense)
- Expand golden set before any future default switch
- Optional CI `--compare` vs `eval_baseline_jina.json` on release branches

### Downstream work

- Unblocks embedding-stable work: [0002](0002-graphrag-neo4j-qdrant.md) Phase 2, [0018](0018-telemetry-observability-otel-prometheus.md) Phase 2
- Revisit default only when a model **beats Jina on golden set** without waiver

## Implementation notes

### New artifacts

- None required beyond refreshed `eval_baseline.json`

### Modified artifacts

| Path | Change |
|------|--------|
| `.env.example`, `.env.compose.integration` | Jina production defaults; Qwen3 → experimental comment block |
| `mcp_server/benchmarks/_settings.py` | Jina `_BENCH_EMBED_DEFAULTS` |
| `mcp_server/benchmarks/fixtures/eval_baseline.json` | Jina metrics (from frozen snapshot or live verify) |
| `README.md`, `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md` | Embedding table: Jina primary |
| `scripts/run_compose_integration.py` | Jina generator defaults |
| `docs/adr/README.md` | Index 0021; note 0016 superseded |
| `docs/adr/0016-qwen3-embedding-default-dense-model.md` | Add superseded-by link in header (status text unchanged — historical record) |
| `docs/adr/0020-qwen3-code-finetune-jina-quality-gate.md` | Measured outcomes: gate failed; phases 2–4 cancelled |

### Rollout

**Breaking revert** for Qwen3-default deployments:

1. `ollama pull unclemusclez/jina-embeddings-v2-base-code`
2. Update `.env`: Jina model ids, `DENSE_EMBED_VECTOR_SIZE=768`
3. **Drop and re-index** every collection

### Data migration

**Yes — full re-index.** Jina 768-dim vectors incompatible with Qwen3 1024-dim collections.

## Validation

### Automated tests

- **Unit** — `test_config.py`: Jina defaults validate; Qwen3 still accepted as optional registry entry
- **Unit** — existing Ollama/Jina tests unchanged
- **Integration** — optional live Jina Ollama probe in manual checklist

### Fixture-based evaluation

- **Target:** `eval_baseline_jina.json` — recall@10 **0.660256** (reference)
- **After Phase 2:** live `eval_retrieval` on Jina re-indexed collection ≥ frozen snapshot (±2 pp tolerance for index drift)

### Success criteria

1. `.env.example` and `_settings.py` default to Jina @ 768
2. `eval_baseline.json` params show `jinaai/jina-embeddings-v2-base-code`
3. Docs present Qwen3 only under experimental preset with ADR 0016 regression citation
4. ADR 0016 linked as superseded for **default policy**; 0020 phases 2–4 marked cancelled in tracker

## Measured outcomes

Evidence chain supporting this revert (no new eval required to adopt decision):

| Variant | recall@10 | MRR | NDCG@10 | Source |
|---------|-----------|-----|---------|--------|
| **Jina (production target)** | **0.660** | **0.587** | **0.539** | `eval_baseline_jina.json` / ADR 0016 |
| Qwen3 base (retired default) | 0.244 | 0.262 | 0.191 | `eval_baseline.json` post–0016 P2 |
| Qwen3 fine-tune (ADR 0020) | *not promoted* | — | — | Gate policy: base failed; no checkpoint passed Phase 3 |

**Per-tag recall@10 (why Jina fits this repo):**

| Tag | Jina | Qwen3 | Interpretation |
|-----|------|-------|----------------|
| conceptual | 0.810 | 0.190 | NL + architecture questions — primary agent use case |
| symbol | 0.722 | 0.278 | Definition / identifier lookup |
| config | 0.400 | 0.000 | Settings and env discovery |
| cross_file | 0.600 | 0.500 | Acceptable Qwen3; still loses |
| multi_hop | 0.500 | 0.333 | Client eval slice |

### Maintainer checklist

1. Re-index golden collection **before** committing refreshed `eval_baseline.json`
2. Run `eval_retrieval --validate-labels` after re-index
3. Do not delete `eval_baseline_jina.json` — keep as historical gate reference
4. When documenting Qwen3 experimental preset, cite **−63.1% recall@10** — do not cite CoIR alone
