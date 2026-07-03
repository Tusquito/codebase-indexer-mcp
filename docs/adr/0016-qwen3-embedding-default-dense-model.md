# 0016. Adopt Qwen3-Embedding-4B as default Ollama dense model

- **Status:** Proposed
- **Date:** 2026-07-03
- **Deciders:** Maintainers
- **Related:** [0011](0011-ollama-only-dense-embedding.md) — Ollama-only dense path; [0017](0017-model-tokenizer-ollama-dense-truncation.md) — model-accurate pre-truncation for long Qwen3 inputs; [0007](0007-ranx-retrieval-evaluation.md) — golden-set eval; [0003](0003-hybrid-search-rrf-default.md) — hybrid dense + BM25; [CoIR leaderboard](https://mteb-leaderboard.hf.space/benchmarks?q=code) — code retrieval benchmark used for comparison

## Context

The project defaults to **`nomic-ai/nomic-embed-text-v1.5`** (768 dims) via Ollama for dense vectors, with in-process BM25 sparse ([ADR 0011](0011-ollama-only-dense-embedding.md)). Operators who enable **GPU-backed Ollama** (`docker-compose.ollama.gpu.yml`) still inherit a model chosen primarily for CPU efficiency and small download size (~274 MB), not for code-retrieval quality.

### Current situation and measurable gap

| Model | CoIR mean (approx.) | Context | Ollama | GPU VRAM (approx.) |
|-------|---------------------|---------|--------|--------------------|
| `nomic-embed-text` v1.5 (current default) | Not in CoIR top 20 | 8K | ✅ | ~0.3 GB |
| **`qwen3-embedding:4b`** (candidate) | **~79.2** (#5 on CoIR) | 40K | ✅ | ~2.5 GB |
| `qwen3-embedding:8b` | Higher on general MTEB; check CoIR row | 40K | ✅ | ~5 GB |
| `codefuse-ai/F2LLM-v2-4B` | ~79.6 (#4 on CoIR) | — | ❌ HF only | — |
| `voyageai/voyage-code-3` | ~78.5 (#7 on CoIR) | 32K | ❌ API | — |

CoIR (Code Information Retrieval) is the most relevant public benchmark for this project: natural-language and hybrid queries over code corpora. On that benchmark, **Qwen3-Embedding-4B is the highest-ranked model available in the official Ollama library** without custom GGUF conversion or a commercial API.

Nomic remains adequate for CPU-only or tight-VRAM setups; it is no longer the best default when a GPU is available for Ollama.

### Hard constraints

1. **Dense vectors must come from Ollama** ([ADR 0011](0011-ollama-only-dense-embedding.md)) — no HuggingFace in-process path for F2LLM/C2LLM without abandoning the established deployment model.
2. **Hybrid search unchanged** — sparse BM25 stays in MCP; only the dense encoder changes.
3. **Dimension change requires full re-index** — Qdrant collections are keyed by `DENSE_EMBED_VECTOR_SIZE`; vectors from Nomic (768) are not comparable to Qwen3 (1024 with MRL).
4. **Bundled Ollama GPU is optional** — the new default assumes GPU is available or acceptable at ~2.5 GB model size; CPU fallback remains supported but slower.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Infrastructure (Ollama probe, dimension match) | yes | Preload must validate output dims |
| Component recall (golden set, ranx) | yes | Refresh `eval_baseline.json` after re-index |
| CoIR / MTEB leaderboard alignment | partial | Informs model choice; not a CI gate |
| End-user agent outcome (Ragas) | no | [ADR 0010](0010-defer-ragas-to-client.md) |

### Why now

- GPU Ollama is already supported and documented ([ADR 0011](0011-ollama-only-dense-embedding.md), `docker-compose.ollama.gpu.yml`).
- Golden-set eval harness ([ADR 0007](0007-ranx-retrieval-evaluation.md), `eval_multihop.py`) exists to validate the switch.
- Recent baselines were captured with Nomic; continuing to treat Nomic as the “GPU default” misleads operators who enable `OLLAMA_GPU=1`.

## Decision

We will **change the recommended default dense embedding model from Nomic Embed Text v1.5 to Qwen3-Embedding-4B** (`qwen3-embedding:4b` on Ollama), storing **1024-dimensional** dense vectors via Matryoshka truncation (MRL).

### In scope

| Area | Change |
|------|--------|
| Default env | `.env.example`, `.env.compose.integration`, benchmark `_settings.py` — `DENSE_EMBED_MODEL=Qwen/Qwen3-Embedding-4B`, `OLLAMA_EMBED_MODEL=qwen3-embedding:4b`, `DENSE_EMBED_VECTOR_SIZE=1024` |
| Model registry | `KNOWN_EMBED_MODEL_DIMENSIONS` and `KNOWN_EMBED_MODEL_MAX_TOKENS` in `config.py` for Qwen3 0.6B / 4B / 8B |
| Ollama backend | Pass MRL `dimensions` (or equivalent) on `/api/embed` / `/v1/embeddings` when `DENSE_EMBED_VECTOR_SIZE` is below native (2560 for 4B) |
| Truncation default | `MAX_DENSE_EMBED_TOKENS` auto-detect → 32768 (or 40960 per Ollama card) for Qwen3 models |
| Docs | `ARCHITECTURE.md`, `DEPLOYMENT.md`, README embedding table — Qwen3 as primary; Nomic as low-VRAM / CPU preset |
| Tests | `test_config.py` known-model validation; Ollama backend mock tests for `dimensions` payload |
| Eval baselines | Re-run `eval_retrieval.py` / `eval_multihop.py` after re-index; commit updated `eval_baseline.json` |
| Operator migration | Document `ollama pull qwen3-embedding:4b` + force re-index |

### Out of scope

- Replacing Ollama with HuggingFace for CodeFuse F2LLM-v2 (top CoIR scores; not in Ollama library)
- Commercial APIs (`voyage-code-3`, Voyage 4, OpenAI embeddings)
- Automatic re-index on model change ([ADR 0011](0011-ollama-only-dense-embedding.md) deferral stands)
- Changing sparse model, hybrid RRF, or ColBERT defaults
- Qwen3 **reranker** models (`qwen3-reranker:*`) — separate from dense embedder; may be a follow-up ADR

### Default behavior and configuration

- **Default:** **breaking for new installs** — fresh `.env` from `.env.example` targets Qwen3-4B @ 1024 dims. Existing deployments keep their env until operators opt in.
- **GPU assumption:** document that the default preset expects **`OLLAMA_GPU=1`** + `docker-compose.ollama.gpu.yml` for acceptable index throughput; Nomic preset remains documented for CPU-only.

| Variable | New recommended value | Notes |
|----------|----------------------|-------|
| `DENSE_EMBED_MODEL` | `Qwen/Qwen3-Embedding-4B` | HF id for registry / validation |
| `OLLAMA_EMBED_MODEL` | `qwen3-embedding:4b` | Ollama pull tag |
| `DENSE_EMBED_VECTOR_SIZE` | `1024` | MRL truncation; native 2560 optional for max quality |
| `MAX_DENSE_EMBED_TOKENS` | `0` (auto) | Registry entry: 32768 |
| `OLLAMA_GPU` | `1` | Recommended with bundled Ollama |

**Presets (documented, not code-enforced):**

| Preset | `OLLAMA_EMBED_MODEL` | `DENSE_EMBED_VECTOR_SIZE` | When |
|--------|----------------------|---------------------------|------|
| **Default (GPU)** | `qwen3-embedding:4b` | `1024` | Code search with GPU Ollama |
| Max quality | `qwen3-embedding:8b` | `1024` or `4096` | 16 GB+ VRAM |
| Low VRAM | `qwen3-embedding:0.6b` | `1024` | ~8 GB GPU |
| CPU / minimal | `nomic-embed-text` | `768` | No GPU; smallest download |

### Phased delivery

1. **Phase 1 — Config, Ollama MRL, docs, tests** — registry, `dimensions` passthrough, `.env.example`, unit tests; no baseline refresh required to merge.
2. **Phase 2 — Eval baseline refresh** — re-index golden fixture collection with Qwen3-4B; update `eval_baseline.json` and `multi_hop_2hop` snapshot; record deltas in tracker.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Qwen3-Embedding-4B @ 1024 dims (chosen)** | #5 on CoIR; official Ollama; 40K context; fits 12 GB GPU; MRL keeps Qdrant storage reasonable | Slower index than Nomic; ~2.5 GB pull; requires `dimensions` API support; full re-index |
| **Status quo (Nomic v1.5)** | Smallest, fastest, proven in repo baselines | Poor CoIR rank; wastes GPU headroom; requires `search_document:` / `search_query:` prefixes |
| **Qwen3-Embedding-8B** | Highest Qwen3 quality | ~5 GB VRAM; marginal CoIR gain over 4B; slower indexing |
| **Qwen3-Embedding-0.6B** | Sub-1 GB; still beats Nomic on MTEB | Not CoIR top-10; compromise if 4B VRAM is tight |
| **embeddinggemma** | Strong MTEB Code v1 (small) | 2K context; lower CoIR tier; worse long-chunk coverage |
| **codefuse-ai/F2LLM-v2-4B** | Slightly above Qwen3-4B on CoIR | Not in Ollama; breaks ADR 0011 deployment model |
| **voyage-code-3 / Voyage 4** | Strong code retrieval; API simplicity | Per-token cost; data leaves host; not Ollama |

## Consequences

### Positive

- Default dense model aligns with **code-retrieval benchmarks** and GPU Ollama deployments
- **40K context** reduces silent truncation vs Nomic’s effective limits on long chunks
- Qwen3 is **instruction-aware** — no Nomic-style mandatory `search_document:` / `search_query:` prefixes (optional task instructions may still help ~1–5% per Qwen docs)
- Stays within [ADR 0011](0011-ollama-only-dense-embedding.md) — single Ollama dense path, hybrid BM25 unchanged

### Negative / trade-offs

- **Breaking for operators on Nomic** — must pull new model, update env, **force re-index** all collections
- **Higher VRAM and index time** — 4B model vs 137M Nomic; bulk indexing slower even on GPU
- **Qdrant storage** — 1024 dims vs 768 (+33% dense storage per point vs Nomic at full dim)
- **Implementation gap today** — `OllamaDenseBackend` does not yet send MRL `dimensions`; native 4B output is 2560 unless fixed
- **Community maturity** — Nomic has more Ollama pulls; Qwen3 embedding is newer (~2.3M pulls vs ~74M for Nomic)

### Neutral / follow-ups

- Optional **Qwen3 reranker** sidecar (query-time) for another ~5–10% on code tasks — evaluate separately
- **F2LLM-v2** on HuggingFace if Ollama path is ever relaxed
- Nomic preset remains valid for CI without GPU and for operators who prefer minimal resource use

### Downstream work

- Refresh golden-set baselines ([ADR 0007](0007-ranx-retrieval-evaluation.md))
- Update multi-hop eval snapshot ([ADR 0009](0009-multi-hop-retrieval-strategies.md))
- Consider ADR for Qwen3 reranker integration (optional query stage)

## Implementation notes

### New artifacts

- None required beyond updated baseline JSON after Phase 2

### Modified artifacts

| Path | Change |
|------|--------|
| `mcp_server/src/codebase_indexer/config.py` | Qwen3 entries in `KNOWN_EMBED_MODEL_*` |
| `mcp_server/src/codebase_indexer/indexer/backends/ollama_dense.py` | Pass `dimensions` when MRL size < native; optional `num_ctx` for long context |
| `mcp_server/src/codebase_indexer/indexer/backends/factory.py` | Wire `vector_size` into Ollama payload |
| `.env.example`, `.env.compose.integration` | New defaults + Nomic CPU preset comment block |
| `mcp_server/benchmarks/_settings.py` | Benchmark default embed model |
| `mcp_server/tests/test_config.py` | Qwen3 dimension validation cases |
| `docs/ARCHITECTURE.md`, `docs/DEPLOYMENT.md`, `README.md` | Default model docs |
| `mcp_server/benchmarks/eval_baseline.json` | Phase 2 refresh |

### Dependencies

- **Runtime:** Ollama with `qwen3-embedding:4b` pulled; GPU recommended (`docker-compose.ollama.gpu.yml`)
- **Optional:** Re-run eval scripts after re-index for baseline commit

### Rollout

**Breaking for adopters of new defaults.** Operators migrating from Nomic:

1. `ollama pull qwen3-embedding:4b`
2. Update `.env`: `DENSE_EMBED_MODEL`, `OLLAMA_EMBED_MODEL`, `DENSE_EMBED_VECTOR_SIZE=1024`
3. Enable GPU Ollama if not already (`OLLAMA_GPU=1`, compose override)
4. **Drop and re-index** every collection (dimension mismatch — Qdrant collection recreate)

### Data migration

**Yes — full re-index required.** Existing Nomic (768-dim) vectors cannot be queried with Qwen3 (1024-dim) embeddings. No in-place vector migration.

## Validation

### Automated tests

- **Unit** — `test_config.py`: Qwen3 4B accepts `DENSE_EMBED_VECTOR_SIZE=1024`; rejects wrong sizes; max-token registry
- **Unit** — `test_ollama_dense_backend.py`: mock verifies `dimensions` in JSON payload when configured
- **Integration** — optional live Ollama probe in manual/smoke checklist (not default CI)

### Fixture-based evaluation

- **Fixtures:** `mcp_server/benchmarks/golden_queries.jsonl` (unchanged labels; re-embed corpus)
- **Metrics:** recall@10, MRR, NDCG@10 ([ADR 0007](0007-ranx-retrieval-evaluation.md))
- **Baseline:** `mcp_server/benchmarks/eval_baseline.json` — refresh in Phase 2; note embed model in snapshot metadata
- **Multi-hop:** `eval_multihop.py` `multi_hop_2hop` slice — compare before/after Nomic vs Qwen3

### Success criteria

1. Ollama preload succeeds with `qwen3-embedding:4b` and returns exactly `DENSE_EMBED_VECTOR_SIZE` (1024) vectors
2. Hybrid index + search completes on a sample collection without dimension errors
3. Golden-set recall@10 on primary slice is **≥ Nomic baseline** (or documented regression with mitigation plan)
4. `.env.example` and deployment docs describe Qwen3 as default and Nomic as CPU preset

## Measured outcomes

*(To be filled after Phase 2 baseline on golden set — 2026-07-03.)*

| Variant | recall@10 | MRR | Notes |
|---------|-----------|-----|-------|
| Nomic v1.5 (current baseline) | TBD | TBD | Existing `eval_baseline.json` |
| Qwen3-4B @ 1024 | TBD | TBD | After re-index |
