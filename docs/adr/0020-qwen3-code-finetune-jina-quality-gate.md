# 0020. Fine-tune Qwen3 for code retrieval with Jina quality gate

- **Status:** Proposed
- **Date:** 2026-07-03
- **Deciders:** Maintainers
- **Related:** [0016](0016-qwen3-embedding-default-dense-model.md) — Qwen3 default and measured Jina regression; [0011](0011-ollama-only-dense-embedding.md) — Ollama-only dense inference; [0007](0007-ranx-retrieval-evaluation.md) — golden-set eval harness; [0017](0017-model-tokenizer-ollama-dense-truncation.md) — tokenizer-accurate truncation; [CoIR leaderboard](https://mteb-leaderboard.hf.space/benchmarks?q=code) — public code-retrieval benchmark
- **Supersedes:** *(none — narrows ADR 0016 rollout policy when quality gate fails)*

## Context

[ADR 0016](0016-qwen3-embedding-default-dense-model.md) adopted **Qwen3-Embedding-4B** as the recommended default dense model based on **CoIR** (#5 among Ollama-available code embedders) and 40K context. Phase 2 measured the switch on this repo’s golden set ([ADR 0007](0007-ranx-retrieval-evaluation.md)) and found a **large regression vs the prior Jina baseline**:

| Variant | recall@10 | MRR | NDCG@10 | Date |
|---------|-----------|-----|---------|------|
| **Jina v2 base code** (`jinaai/jina-embeddings-v2-base-code`) | **0.660** | **0.587** | **0.539** | 2026-07-02 |
| Qwen3-4B @ 1024 MRL (hybrid) | 0.244 | 0.262 | 0.191 | 2026-07-03 |
| Delta | **−63.1%** | −55.2% | −64.5% | |

Phase 2 merged with a **documented waiver** — CoIR rank justified the default for new GPU installs; the golden set was labeled under Jina/Nomic-era chunk boundaries; mitigation was deferred (label refresh, Qwen3 reranker, etc.).

That leaves a **credibility gap**: operators who run `eval_retrieval --compare` see Qwen3 underperform the model this fixture set was tuned for, while public benchmarks claim Qwen3 is among the best code embedders. Before further embedding-track work (GraphRAG payload linking, telemetry, etc.), we need a **repo-grounded path** to make Qwen3 (or a derivative) **measurably competitive with Jina** on our golden set — or revert the default with evidence.

### Current situation and measurable gap

| Signal | Qwen3 base | Jina v2 code | Gap |
|--------|------------|--------------|-----|
| CoIR / MTEB code rank | Top tier (#5 CoIR) | Strong code model; not CoIR top-5 | Public benchmarks favor Qwen3 |
| Repo golden recall@10 | 0.244 | 0.660 | **−63%** — fixture-grounded |
| `config` tag recall@10 | 0.000 | 0.400 | Complete failure on env/config queries |
| `symbol` tag recall@10 | 0.278 | 0.722 | Identifier-shaped queries hurt |
| Multi-hop 2-hop RRF recall@10 | 0.167 | 0.333 | Client fusion also regressed |

**Hypothesis:** Base Qwen3 is general-purpose and instruction-aware; it was not optimized for **this repo’s chunking scheme, query phrasing, and hybrid RRF ranking** the way the golden set encodes “good” retrieval. **Supervised fine-tuning** on query–passage pairs derived from the golden set (plus hard negatives from live misses) can close much of the gap without abandoning Qwen3’s context length and Ollama deployment model.

### Hard constraints

1. **Inference stays Ollama-only** ([ADR 0011](0011-ollama-only-dense-embedding.md)) — training may use HuggingFace offline; production dense vectors must be served via Ollama HTTP (`/api/embed` with MRL `dimensions`).
2. **Hybrid search unchanged** — only the dense encoder changes; sparse BM25 and RRF fusion stay as-is ([ADR 0003](0003-hybrid-search-rrf-default.md)).
3. **Dimension change requires full re-index** — fine-tuned model must output the same `DENSE_EMBED_VECTOR_SIZE` (1024 MRL recommended) or operators re-index again.
4. **Training is maintainer/offline** — no GPU training in default CI or MCP runtime image; optional `[benchmark]` / `[train]` extras only.
5. **Quality gate is mandatory** — unlike ADR 0016 Phase 2, **no default promotion** until fine-tuned Qwen3 **meets or exceeds Jina** on the committed golden set (overall + per-tag criteria below).

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Training loss / validation IR metrics | yes | Offline; InfoNCE / triplet on curated pairs |
| Golden-set ranx (layer 2) | yes | Primary **accept/reject gate** vs Jina snapshot |
| CoIR / MTEB leaderboard | partial | Sanity check; not sufficient alone |
| Multi-hop 2-hop RRF | yes | `eval_multihop.py` — same gate as single-pass |
| End-user Ragas (layer 3) | no | [ADR 0010](0010-defer-ragas-to-client.md) |
| ANN recall (layer 1) | no | Document operational check only |

### Why now

- ADR 0016 is **Accepted (all phases complete)** with a known Jina regression — continuing the embedding track without closing the gap risks operator distrust.
- Golden set, eval harness, and Jina comparison baseline already exist — marginal cost to add a **head-to-head gate** is low.
- Qwen3 base weights and LoRA tooling (PEFT, sentence-transformers, Qwen official embedding fine-tune examples) are mature enough for a focused code-retrieval adaptation.

## Decision

We will **fine-tune Qwen3-Embedding-4B on code-retrieval pairs** derived from this repository’s golden set and indexed chunks, package the result as a **custom Ollama embedding model**, and **promote it to the recommended default only after it beats the committed Jina baseline** on golden-set metrics.

Until the gate passes, **Qwen3 base remains the documented default** from ADR 0016; the fine-tuned variant is an **opt-in preset** (`OLLAMA_EMBED_MODEL=codeindexer/qwen3-code:4b-ft` or similar).

### In scope

| Area | Change |
|------|--------|
| Training corpus | Positive pairs from `golden_queries.jsonl` (`query_text` → labeled `chunk_id` content); hard negatives from Qwen3 top-k misses; optional in-batch negatives |
| Fine-tune method | LoRA (or QLoRA) on `Qwen/Qwen3-Embedding-4B`; contrastive / InfoNCE loss; max seq length aligned with indexer truncation ([ADR 0017](0017-model-tokenizer-ollama-dense-truncation.md)) |
| Offline tooling | `mcp_server/benchmarks/train/` (or `scripts/finetune/`) — dataset export, train script, eval-on-checkpoint; optional `[train]` extra in `pyproject.toml` |
| Ollama packaging | Export merged weights → GGUF or Ollama `Modelfile` import; document `ollama create` / pull steps in `DEPLOYMENT.md` |
| Registry | Add fine-tuned model entry in `KNOWN_EMBED_MODEL_*` with same native/MRL dims as base Qwen3-4B |
| Eval gate | Side-by-side `eval_retrieval` + `eval_multihop` — fine-tuned Qwen3 vs **frozen Jina snapshot** (`eval_baseline_jina.json`) |
| Baseline artifacts | Commit `eval_baseline_jina.json` (historical Jina metrics, read-only reference); update `eval_baseline.json` only when gate passes |
| Docs | `DEPLOYMENT.md` training section; README preset table row for fine-tuned model |

### Out of scope

- Fine-tuning sparse BM25 or ColBERT ([ADR 0008](0008-optional-colbert-reranking.md))
- Automatic re-index when switching to fine-tuned model ([ADR 0011](0011-ollama-only-dense-embedding.md) deferral stands)
- Multi-repo or customer-specific fine-tunes in the main repo (document pattern; no per-tenant training service)
- Commercial embedding APIs for training or inference
- Replacing golden-set labels entirely without maintainer review — fine-tuning **complements** label refresh, does not skip it
- CI GPU training on every PR

### Default behavior and configuration

- **Default:** **unchanged until gate passes** — `qwen3-embedding:4b` base remains recommended in `.env.example`.
- **After gate passes:** update `.env.example` to fine-tuned Ollama tag; refresh `eval_baseline.json`; note promotion in CHANGELOG.
- **Opt-in during development:**

| Variable | Fine-tune dev value | Notes |
|----------|---------------------|-------|
| `DENSE_EMBED_MODEL` | `Qwen/Qwen3-Embedding-4B` | Same HF id; registry distinguishes `…-code-ft` suffix if needed |
| `OLLAMA_EMBED_MODEL` | `codeindexer/qwen3-code:4b-ft` | Custom Ollama model from merged LoRA weights |
| `DENSE_EMBED_VECTOR_SIZE` | `1024` | MRL; must match training export |
| `RERANK_ENABLED` | `false` | Gate runs without ColBERT for parity with Jina/Qwen3 base comparisons |

### Phased delivery

1. **Phase 1 — Dataset + training pipeline** — export golden pairs; hard-negative mining script; LoRA train script; validation split; checkpoint selection by validation MRR.
2. **Phase 2 — Ollama export + registry** — merge LoRA; convert/import to Ollama; preload smoke test; `KNOWN_EMBED_MODEL_*` entry; unit tests for registry validation.
3. **Phase 3 — Jina quality gate + baseline commit** — re-index golden collection with fine-tuned model; run `eval_retrieval` + `eval_multihop`; compare vs `eval_baseline_jina.json`; commit updated `eval_baseline.json` and ADR **Measured outcomes** only if gate passes; otherwise document failure and keep base Qwen3 default.
4. **Phase 4 — Optional CI observation job** — non-blocking workflow running eval compare on release branches when fine-tuned model artifact is available (no GPU train in CI).

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **LoRA fine-tune Qwen3 + Jina gate (chosen)** | Closes domain gap; keeps Qwen3 context + Ollama path; objective accept/reject | GPU time; packaging friction; may still miss Jina on some tags |
| **Golden label refresh only** ([ADR 0007](0007-ranx-retrieval-evaluation.md) checklist) | Lower effort; fixes alias/chunk drift | Does not fix encoder mismatch; Qwen3 still −63% after remapping in Phase 2 |
| **Revert default to Jina in Ollama** | Immediately restores fixture recall | Loses 40K context and CoIR rank; community Jina Ollama port (`unclemusclez/jina-embeddings-v2-base-code`); 768-dim re-index |
| **Qwen3 reranker only** | Query-time boost; no re-embed corpus | Does not fix index-time dense recall; extra latency and sidecar scope |
| **Trust CoIR; ignore golden set** | Simple | Operators running local eval see regression; undermines ADR 0007 investment |
| **Full fine-tune (no LoRA)** | Maximum capacity | VRAM and storage cost; harder Ollama quantize; overkill for 26-query golden set |
| **Train Jina instead of Qwen3** | Directly optimizes incumbent winner | Jina not in official Ollama library; smaller context; abandons Qwen3 roadmap |

## Consequences

### Positive

- **Evidence-based default** — promotion requires beating Jina on the same harness used since [ADR 0007](0007-ranx-retrieval-evaluation.md)
- Reuses golden labels as **supervision** — turns evaluation debt into training signal
- Stays within **Ollama-only inference** ([ADR 0011](0011-ollama-only-dense-embedding.md))
- Establishes a **repeatable pattern** for future embed model changes (export → re-index → compare vs frozen baseline)

### Negative / trade-offs

- **Maintainer GPU burden** — training and export not runnable on typical CI runners
- **Small golden set (26 queries)** — risk of overfitting; mitigated by hard negatives, validation holdout, and optional public code-retrieval pairs
- **Ollama custom model friction** — operators must `ollama create` or pull a maintainer-built artifact; not in official Ollama library until published
- **Two Qwen3 variants to document** — base vs fine-tuned until gate passes and promotion completes

### Neutral / follow-ups

- Expand training data with public code search corpora (CodeSearchNet, CoIR dev splits) if golden-only training overfits
- Combine fine-tuned dense + Qwen3 reranker if gate passes narrowly on aggregate but fails one tag
- Revisit ADR 0016 measured outcomes section with final fine-tuned deltas

### Downstream work

- ADR 0016 — update default recommendation if fine-tuned model promoted
- [0002](0002-graphrag-neo4j-qdrant.md) Phase 2 — prefer stable embed quality before payload linking tuning
- [0008](0008-optional-colbert-reranking.md) — measure rerank lift on fine-tuned dense baseline

## Implementation notes

### New artifacts

| Path | Purpose |
|------|---------|
| `mcp_server/benchmarks/fixtures/eval_baseline_jina.json` | Frozen Jina metrics (2026-07-02); read-only comparison target |
| `mcp_server/benchmarks/train/export_golden_pairs.py` | Golden JSONL → contrastive JSONL/JSON |
| `mcp_server/benchmarks/train/mine_hard_negatives.py` | Top-k Qwen3 misses → negative passages |
| `mcp_server/benchmarks/train/finetune_qwen3_code.py` | LoRA training entrypoint |
| `mcp_server/benchmarks/train/export_ollama.md` | Merge + GGUF / Modelfile steps |
| `docs/DEPLOYMENT.md` | § Fine-tuned embedding model |

### Modified artifacts

| Path | Change |
|------|--------|
| `mcp_server/pyproject.toml` | Optional `[train]` extra (`peft`, `datasets`, `accelerate`, …) |
| `mcp_server/src/codebase_indexer/config.py` | Registry entry for fine-tuned model id |
| `.env.example` | Comment block for fine-tuned preset (opt-in until promotion) |
| `mcp_server/benchmarks/fixtures/eval_baseline.json` | Updated only when Phase 3 gate passes |
| `mcp_server/benchmarks/eval_retrieval.py` | Optional `--compare-jina` shorthand vs `eval_baseline_jina.json` |

### Dependencies

- **Runtime:** unchanged — Ollama HTTP only
- **Training (optional):** PyTorch, PEFT, HuggingFace `transformers` / `sentence-transformers`, CUDA GPU (≥16 GB recommended for 4B LoRA)
- **Export:** `llama.cpp` convert or Ollama import tooling; maintainer-run

### Training data schema (sketch)

```json
{
  "query_id": "q_embedder_class",
  "query": "class Embedder embedder.py dense sparse hybrid",
  "positive": "<chunk text from labeled chunk_id>",
  "negatives": ["<hard negative passage>", "..."],
  "tags": ["symbol"]
}
```

- **Positives:** scroll Qdrant or read from indexer cache for labeled `chunk_id` at alias line
- **Negatives:** top-10 Qwen3 base results not in `labels`; plus random in-batch negatives
- **Holdout:** reserve 4 queries (e.g. all `multi_hop` or stratified 15%) for validation MRR during training

### Rollout

**Opt-in until gate passes.** Promotion steps after Phase 3 success:

1. Update `.env.example` defaults to fine-tuned Ollama tag
2. Commit `eval_baseline.json` with fine-tuned metrics
3. CHANGELOG entry; ADR 0020 → Accepted; cross-link ADR 0016 measured outcomes

If gate **fails:** keep base Qwen3 default; publish measured outcomes in this ADR; open decision: expand training data vs revert default to Jina preset.

### Data migration

**Yes — full re-index** when switching from base Qwen3 to fine-tuned weights (same 1024 dims but incompatible vector space). Operators re-index after `ollama pull` / `ollama create` of fine-tuned model.

## Validation

### Automated tests

- **Unit** — dataset export produces valid pairs for every golden entry with resolvable labels
- **Unit** — registry accepts fine-tuned model id + `DENSE_EMBED_VECTOR_SIZE=1024`
- **Unit** — mock Ollama backend: fine-tuned tag resolves same dimension as base Qwen3-4B
- **Integration** — optional `@pytest.mark.slow`: load checkpoint, embed one query, assert dim 1024 (skipped in default CI)

### Fixture-based evaluation

- **Fixtures:** `golden_queries.jsonl` (unchanged queries; re-embed corpus with fine-tuned model)
- **Compare target:** `eval_baseline_jina.json` — Jina recall@10 **0.660256** (2026-07-02)
- **Metrics:** recall@10, MRR, NDCG@10; `metrics_by_tag`; `multi_hop_2hop` snapshot
- **Config parity:** hybrid ON, `RERANK_ENABLED=false`, `top_k=10`, same collection

### Success criteria (Phase 3 gate — all required)

1. **Overall:** fine-tuned Qwen3 hybrid **recall@10 ≥ 0.660** (Jina baseline) — no waiver
2. **Per-tag:** fine-tuned recall@10 **≥ Jina** for each tag with ≥2 queries (`symbol`, `conceptual`, `config`, `cross_file`, `multi_hop`); if one tag fails by ≤5 pp, document mitigation and require aggregate win ≥5 pp
3. **Multi-hop:** `multi_hop_2hop` recall@10 **≥ 0.333** (Jina 2026-07-02)
4. **No tag at zero:** `config` tag recall@10 **> 0** (Qwen3 base scored 0.0)
5. Ollama preload returns exactly **1024** dimensions with MRL configured
6. Training validation MRR on holdout queries **improves ≥10%** vs Qwen3 base embeddings on same holdout (sanity against pure overfit)

### CI adoption

- **Default:** no training in CI
- **Phase 4 optional:** non-blocking eval compare job on release branches when fine-tuned Ollama model is pre-pulled on self-hosted runner
- **Do not** gate every PR on fine-tuned eval until baseline stabilizes post-promotion

## Measured outcomes

*(Empty — fill after Phase 3 head-to-head eval. Record fine-tuned vs Jina vs Qwen3 base in a dated table.)*

### Baseline summary (reference — not yet run)

| Variant | recall@10 | MRR | NDCG@10 | Notes |
|---------|-----------|-----|---------|-------|
| Jina v2 code (gate target) | 0.660256 | 0.586538 | 0.538681 | `eval_baseline_jina.json` |
| Qwen3-4B base (current default) | 0.243590 | 0.262286 | 0.190977 | ADR 0016 Phase 2 |
| Qwen3-4B code fine-tune | *TBD* | *TBD* | *TBD* | Phase 3 |

### Maintainer checklist

1. Export pairs only after `validate-labels` passes on indexed collection
2. Mine hard negatives from **base** Qwen3, not fine-tuned checkpoint, to avoid circular negatives
3. Compare with identical `indexed_at` corpus — re-index once, run Jina (reference), base Qwen3, and fine-tuned in same session if possible
4. Record train hyperparameters (LoRA rank, lr, epochs) in tracker for reproducibility
