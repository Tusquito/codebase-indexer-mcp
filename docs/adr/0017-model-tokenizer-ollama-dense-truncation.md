# 0017. Model-accurate tokenizer for Ollama dense truncation

- **Status:** Accepted (phase 1 — loader + Ollama backend)
- **Date:** 2026-07-03
- **Deciders:** Maintainers
- **Related:** [0011](0011-ollama-only-dense-embedding.md) — Ollama-only dense path; [0016](0016-qwen3-embedding-default-dense-model.md) — Qwen3 32K context default; `indexer/truncation.py` — shared truncation helpers

## Context

Dense vectors are produced by **Ollama HTTP** ([ADR 0011](0011-ollama-only-dense-embedding.md)). Before each `/api/embed` call, MCP may cap input length via `MAX_DENSE_EMBED_TOKENS` (auto from `KNOWN_EMBED_MODEL_MAX_TOKENS` when `0`).

### Current situation and measurable gap

Today `OllamaDenseBackend._truncate_batch` uses **`truncate_bm25_text`** — the same word-split heuristic as sparse BM25 (`SimpleTokenizer.tokenize`). That is **not** the tokenizer Ollama uses for Nomic, Qwen3, or Jina models.

| Aspect | BM25 word-split (current) | Model tokenizer (proposed) |
|--------|---------------------------|----------------------------|
| Token count vs Ollama | Often 2–4× off on code (identifiers, punctuation, unicode) | Matches embedder vocabulary |
| Truncation boundary | Word boundaries | Subword / BPE boundaries |
| Risk on long chunks | Under-truncate → silent Ollama clip; over-truncate → lost code context | Predictable cap at `max_tokens` |
| Qwen3 @ 32K | Word cap of 32768 can still exceed real token limit on dense code | Accurate pre-flight before HTTP |

Sparse ONNX and ColBERT backends already truncate with a **HuggingFace `tokenizers.Tokenizer`** extracted from the FastEmbed model cache (`onnx_sparse.py`, `colbert_onnx.py`). Only the Ollama dense path is inconsistent.

[ADR 0011](0011-ollama-only-dense-embedding.md) documents dense truncation as a **“word-split approximation”** — acceptable when Nomic’s effective limit was modest and chunks were short. [ADR 0016](0016-qwen3-embedding-default-dense-model.md) raises the default context window to **32K+ tokens**, making approximation errors materially worse: more index payload reaches Ollama, and silent server-side truncation hurts retrieval quality without logs.

### Hard constraints

1. **No in-process dense inference** — tokenizer is for **truncation only**; embeddings stay on Ollama ([ADR 0011](0011-ollama-only-dense-embedding.md)).
2. **MCP image size** — avoid pulling PyTorch or full `transformers` just to count tokens.
3. **Offline / air-gapped** — tokenizer files must cache under `HF_HOME` (or explicit path) after first download; failure must degrade gracefully.
4. **Hybrid search unchanged** — sparse BM25 keeps its own truncation path.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Truncation correctness (token count vs HF reference) | yes | Unit tests with cached tokenizer fixtures |
| Index/search latency | partial | One-time preload; per-chunk encode cost |
| Golden-set recall | partial | Indirect — fewer silently truncated chunks |
| End-user agent outcome | no | [ADR 0010](0010-defer-ragas-to-client.md) |

### Why now

- Qwen3 default ([ADR 0016](0016-qwen3-embedding-default-dense-model.md)) increases the cost of wrong truncation.
- `truncate_with_tokenizer` and registry-driven `resolve_max_embed_tokens` already exist — Ollama dense only needs to wire them.
- `tokenizers` is already a transitive dependency of `qdrant-client[fastembed]`; no new heavy stack required.

## Decision

We will **replace BM25 word-split truncation in `OllamaDenseBackend` with model-accurate truncation using the HuggingFace `tokenizers` library**, loaded from `DENSE_EMBED_MODEL` (HF repo id).

We will **not** add `transformers.AutoTokenizer` as a runtime dependency.

### Rationale: `tokenizers` over `AutoTokenizer`

`AutoTokenizer.from_pretrained(model_id)` is the right *concept* (match the embedder’s vocabulary) but the wrong *package* for this server:

| | `tokenizers.Tokenizer.from_pretrained` | `transformers.AutoTokenizer` |
|--|----------------------------------------|------------------------------|
| Tokenization parity | Same Rust core for standard HF models | Same for most models |
| Extra deps | Already present via fastembed | Adds `transformers`; often tempts `torch` |
| MCP container impact | ~few MB tokenizer JSON per model | Hundreds of MB class stack |
| API fit | `encode` / `decode` — already used in `truncation.py` | Richer (chat templates, padding) — unused here |

**Conclusion:** Model-accurate HF tokenization **yes**; **`AutoTokenizer` specifically no** — use `tokenizers` directly.

### In scope

| Area | Change |
|------|--------|
| `OllamaDenseBackend` | Lazy-load shared `Tokenizer` from `DENSE_EMBED_MODEL` at preload; call `truncate_for_embedding` instead of `truncate_bm25_text` |
| Tokenizer loader | Small helper (e.g. `load_dense_tokenizer(model_id) -> Tokenizer \| None`) with HF Hub download + env cache dir |
| Fallback | If download/load fails, log warning and skip truncation (`max_tokens` treated as best-effort) or retain char-based safety cap — document chosen behavior in implementation |
| Config | Optional `HF_HOME` / `TRANSFORMERS_CACHE` passthrough in compose for persistent tokenizer cache |
| Tests | Mock tokenizer for unit tests; optional slow test with real `nomic-ai/nomic-embed-text-v1.5` tokenizer |
| Docs | Update [ADR 0011](0011-ollama-only-dense-embedding.md) config table — remove “word-split approximation” |

### Out of scope

- Loading full Qwen / Nomic **weights** in MCP
- `transformers` chat templates or instruction prefixes (Qwen3 task strings remain optional operator concern)
- Changing sparse BM25 truncation
- Ollama-side `num_ctx` negotiation (separate from client-side text cap)
- Bundling tokenizer files inside the Docker image (cache on first run instead)

### Default behavior and configuration

- **Default:** **behavior change, config unchanged** — `MAX_DENSE_EMBED_TOKENS=0` still auto-resolves from registry; truncation becomes model-accurate when tokenizer loads.
- **Breaking:** None for stored vectors; re-index not required. Long chunks may embed **more** text (if word-split was over-truncating) or **less** (if under-truncating) — quality may shift slightly; golden-set compare recommended after [ADR 0016](0016-qwen3-embedding-default-dense-model.md) rollout.

| Variable | Role |
|----------|------|
| `DENSE_EMBED_MODEL` | HF repo id for tokenizer download (already required) |
| `MAX_DENSE_EMBED_TOKENS` | Cap (unchanged semantics) |
| `HF_HOME` | Tokenizer cache root (optional) |

### Phased delivery

1. **Phase 1 — Loader + Ollama backend** — shared tokenizer, switch `_truncate_batch`, unit tests, graceful fallback.
2. **Phase 2 — Observability** — log `token_count` when truncation fires; optional metric for truncated-chunk rate during index.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **`tokenizers.Tokenizer.from_pretrained` (chosen)** | Accurate; lightweight; matches existing `truncate_with_tokenizer`; already in dep tree | First-run Hub download; air-gap needs pre-seeded cache |
| **`transformers.AutoTokenizer`** | Familiar one-liner; handles edge-case configs | Heavy dependency; inference-adjacent stack for truncation-only use |
| **Status quo (BM25 word-split)** | Zero download; fast | Wrong token counts on code; documented approximation; worse at 32K limits |
| **Char-based heuristic (`len(text) // 4`)** | No deps | Still inaccurate; fails on minified code and unicode |
| **No client truncation (rely on Ollama)** | Simplest MCP code | Silent clip; no index-time logs; unpredictable embed inputs |
| **Extract tokenizer from Ollama model blob** | No HF Hub | Ollama storage layout is not a stable public API; harder to test |

## Consequences

### Positive

- Truncation aligns with **Ollama embedder vocabulary** for any `DENSE_EMBED_MODEL` with a HF tokenizer
- Reuses **`truncate_for_embedding`** — same code path as sparse ONNX and ColBERT
- Enables trustworthy **32K caps** for Qwen3 ([ADR 0016](0016-qwen3-embedding-default-dense-model.md))
- Index logs can report **real token counts** when chunks are clipped

### Negative / trade-offs

- **First-run network** — Hub fetch for `tokenizer.json` (+ merges/vocab) per dense model
- **Memory** — one shared tokenizer in process (~tens of MB for Qwen)
- **CPU per chunk** — `encode` on long strings; mitigated by existing short-text fast path in `truncate_with_tokenizer`
- **Air-gap ops** — must pre-populate HF cache or mount tokenizer files

### Neutral / follow-ups

- Pin tokenizer revision in docs for reproducible offline installs
- If Ollama adds a token-count API, could cross-check client-side counts in debug mode

### Downstream work

- Refresh golden-set baseline if truncation changes materially change embedded text ([ADR 0007](0007-ranx-retrieval-evaluation.md))
- Implement alongside [ADR 0016](0016-qwen3-embedding-default-dense-model.md) Phase 1

## Implementation notes

### New artifacts

| Path | Purpose |
|------|---------|
| `indexer/tokenizer_loader.py` (or `truncation.py` extension) | `load_dense_tokenizer(model_id)`, cache dir resolution |

### Modified artifacts

| Path | Change |
|------|--------|
| `indexer/backends/ollama_dense.py` | Shared tokenizer; `truncate_for_embedding` in `_truncate_batch` |
| `tests/test_ollama_dense_backend.py` | Assert tokenizer-based truncation (mock `Tokenizer`) |
| `tests/test_truncation.py` | Loader fallback / cache miss cases |
| `docs/ARCHITECTURE.md` | Dense truncation behavior |
| `.env.example` | Optional `HF_HOME` note for tokenizer cache |

### Dependencies

- **Runtime:** existing `tokenizers` (via `qdrant-client[fastembed]`); explicit direct dep optional for clarity
- **Not added:** `transformers`, `torch`

### Rollout

**Non-breaking config; behavior refinement.** Operators on air-gapped hosts should run once with network or copy tokenizer files into `HF_HOME` before bulk index.

### Data migration

**No** — re-index not required; embedded text for borderline-length chunks may differ slightly.

## Validation

### Automated tests

- **Unit** — mock `Tokenizer.encode` / offsets; verify `_truncate_batch` respects `max_tokens`
- **Unit** — loader returns `None` on failure; backend logs and passes text through (or documented fallback)
- **Unit** — Qwen3 / Nomic registry entries still resolve max tokens via `KNOWN_EMBED_MODEL_MAX_TOKENS`

### Success criteria

1. Ollama dense backend no longer imports `truncate_bm25_text`
2. Truncation token count matches HF `tokenizers` reference on a fixed code snippet fixture
3. Preload succeeds offline when tokenizer cache is pre-seeded
4. [ADR 0011](0011-ollama-only-dense-embedding.md) config table updated to describe model tokenizer truncation

## Measured outcomes

*(Optional — compare truncated-chunk rate and golden-set recall before/after on Qwen3-4B fixture collection.)*
