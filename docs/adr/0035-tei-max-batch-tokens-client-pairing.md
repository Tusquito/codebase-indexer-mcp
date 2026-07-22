# 0035. Pair TEI `--max-batch-tokens` with client dense truncation

- **Status:** Accepted (phase 1)
- **Date:** 2026-07-22
- **Deciders:** Maintainers
- **Related:** [0025](0025-huggingface-tei-dense-embedding.md) (TEI dense sidecar), [0017](0017-model-tokenizer-tei-dense-truncation.md) (client tokenizer truncation), [0028](0028-apple-silicon-arm64-cpu-deployment.md) (CPU TEI defaults), [0030](0030-migrate-mcp-server-to-dotnet10.md) (Aspire production path), [DEPLOYMENT.md](../DEPLOYMENT.md)

## Context

On Aspire/CPU TEI startup (`codeindexer_tei`), HuggingFace Text Embeddings Inference logs:

> The maximum input length is `8192` which exceeds `--max-batch-tokens=1024`. Input sequences will be truncated to `1024` tokens, as `--auto-truncate` … is true.

That warning is **expected** for the CPU path: [ADR 0025](0025-huggingface-tei-dense-embedding.md) tracked an upstream TEI CPU-warmup failure when `--max-batch-tokens` tracked the model’s full `max_input_length` (8192 for `jinaai/jina-embeddings-v2-base-code`). Compose/AppHost therefore pass `--max-batch-tokens 1024`.

### Measurable gap

The Aspire/.NET stack does **not** keep the client cap in lockstep:

| Surface | Today | Effect |
|---------|-------|--------|
| `AppHost` / `docker-compose.aspire.yml` | Hard-coded `--max-batch-tokens 1024` | TEI server clips every input to 1024 tokens |
| `Embedding:MaxDenseTokens` (`appsettings.json` = `0`) | Auto-resolves to **8192** via Jina registry ([ADR 0017](0017-model-tokenizer-tei-dense-truncation.md)) | MCP may send up to 8192 tokens of text |
| Operator docs (`.env.example`) | Advise `MAX_DENSE_EMBED_TOKENS <= TEI_MAX_BATCH_TOKENS` | Pairing is documented but **not wired** on the Aspire default path |

Result: long chunks are truncated **server-side** with `auto-truncate`, after the client already believed the model context was 8192. That hides loss of code context from index logs and wastes client-side tokenization work.

### Other TEI warnings in the same log (explicitly not this ADR)

| Log line | Verdict |
|----------|---------|
| `404` for `config_sentence_transformers.json` | **Ignore** — optional Sentence-Transformers metadata; TEI continues and loads ONNX weights |
| `Backend does not support a batch size > 8` / `forcing max_batch_requests=8` | **Ignore** — ONNX CPU backend limit; TEI self-corrects |
| `Invalid hostname, defaulting to 0.0.0.0` | **Ignore** — Docker hostname is not a bind address; `0.0.0.0:80` is correct |
| Graceful shutdown mid-run | **Ops lifecycle** — container restart (Aspire/compose), not a config defect |

### Hard constraints

1. CPU TEI cold start must remain reliable (no return to unbounded `--max-batch-tokens` on CPU).
2. Client truncation stays model-tokenizer accurate ([ADR 0017](0017-model-tokenizer-tei-dense-truncation.md)).
3. Production path is Aspire/.NET ([ADR 0030](0030-migrate-mcp-server-to-dotnet10.md)); Python compose overlays are historical.

### Why now

Operators see the TEI WARN on every Aspire start and may raise `--max-batch-tokens` without lowering client caps (or vice versa). Formalizing the pairing closes the silent-truncation gap and documents which warnings are noise.

## Decision

We will **treat TEI `--max-batch-tokens` and MCP `Embedding:MaxDenseTokens` as a paired knob**, defaulting both to **1024** on the Aspire/CPU TEI path, and require `MaxDenseTokens ≤ TEI_MAX_BATCH_TOKENS` whenever either is raised.

We will **not** raise CPU TEI to the model’s full 8192 context by default.

We will **not** silence or work around the optional HF artifact 404, ONNX batch-8 cap, or hostname bind warnings.

### In scope

| Area | Change |
|------|--------|
| Aspire AppHost | Drive `--max-batch-tokens` from `TEI_MAX_BATCH_TOKENS` (default `1024`); stop hard-coding only |
| `docker-compose.aspire.yml` | Same env substitution; set `Embedding__MaxDenseTokens` default `1024` (or `${MAX_DENSE_EMBED_TOKENS:-1024}`) |
| Host defaults | Aspire compose override so production stack does not leave `MaxDenseTokens=0` → registry 8192 while TEI caps at 1024 |
| Docs | `.env.example`, `DEPLOYMENT.md`, four-surface sync: pair rule + “TEI WARN expected when caps match” |
| Validation | Unit/integration assert client cap ≤ TEI batch tokens on default Aspire env |

### Out of scope

- Changing Jina registry `KNOWN_EMBED_MODEL_MAX_TOKENS` (8192 remains the **model** limit)
- Raising GPU TEI to 8192 in this ADR (optional follow-up: raise **both** knobs together on GPU-verified hosts)
- Upstream TEI patches for `config_sentence_transformers.json` or ONNX batch size
- `--auto-truncate false` without raising `--max-batch-tokens` (would turn truncation into request errors)

### Default behavior and configuration

- *Default (Aspire/CPU):* `TEI_MAX_BATCH_TOKENS=1024` and `MAX_DENSE_EMBED_TOKENS` / `Embedding__MaxDenseTokens=1024`
- *Override:* raise both together (e.g. GPU hosts with verified warmup → `8192` / `8192` for Jina)
- *Invariant:* `MaxDenseTokens ≤ TEI_MAX_BATCH_TOKENS` (document; optional fail-fast log if violated)

### Phased delivery

1. **Phase 1 — Wire pairing on Aspire path** — env-driven `--max-batch-tokens`; compose/AppHost client default 1024; docs; tests
2. **Phase 2 — Optional GPU preset** — document/preset raising both knobs to model max when `ACCELERATOR=gpu` and TEI image is CUDA (only after warmup smoke)

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Chosen: pair knobs @ 1024 on CPU Aspire** | Predictable truncation; client logs match server; CPU warmup safe | Leaves ~7/8 of Jina context unused on CPU |
| Raise TEI to 8192, leave client auto | Uses full model context | Reintroduces CPU warmup risk that forced the 1024 cap |
| Status quo (server 1024, client auto 8192) | Zero code | Silent TEI truncation; misleading registry “8192” ops story |
| `--auto-truncate false` only | Fail loud on oversize | Breaks index for any chunk > 1024 unless client already capped |
| Ignore warning in docs only | Cheap | Does not fix client/server mismatch |

## Consequences

### Positive

- Index/search embeds truncate once, on the client, with tokenizer-aware logs ([ADR 0017](0017-model-tokenizer-tei-dense-truncation.md))
- TEI startup WARN becomes an expected confirmation that caps match, not a latent quality bug
- Operators have one documented rule for raising context on GPU

### Negative / trade-offs

- Default dense context stays **1024** tokens on Aspire/CPU even though Jina supports 8192
- `MaxDenseTokens=0` “auto from registry” is unsafe on Aspire unless compose overrides it — auto-detect means **model** max, not **deployed TEI** max

### Neutral / follow-ups

- Chunk line caps (`MAX_CHUNK_LINES=150`) already keep most chunks under 1024 tokens; pairing mainly protects dense/minified outliers
- Consider deriving client default from `TEI_MAX_BATCH_TOKENS` in one place to avoid drift

### Downstream work

- Phase 2 GPU dual-raise preset
- Optional metric for truncated-chunk rate ([ADR 0017](0017-model-tokenizer-tei-dense-truncation.md) Phase 2 / [0018](0018-telemetry-observability-otel-prometheus.md))

## Implementation notes

### Modified artifacts

| Path | Change |
|------|--------|
| `src/CodebaseIndexer.AppHost/AppHost.cs` | `--max-batch-tokens` from `TEI_MAX_BATCH_TOKENS` (default 1024); pass `Embedding__MaxDenseTokens` |
| `docker-compose.aspire.yml` | `${TEI_MAX_BATCH_TOKENS:-1024}` in `command`; `Embedding__MaxDenseTokens: ${MAX_DENSE_EMBED_TOKENS:-1024}` |
| `.env.example` / `docs/DEPLOYMENT.md` | Aspire pairing as default, not comment-only |
| Integration harness | Assert paired defaults |

### Rollout

- Default on merge (pre-release); re-index **not** required for vectors already produced under TEI 1024 auto-truncate (behavior already matched server-side). Re-index only if operators later raise both caps and want longer chunks embedded.

### Data migration

- **No** for the 1024 pairing fix (aligns client with existing TEI behavior)
- **Yes** if Phase 2 raises caps — full re-index to embed previously truncated tails

## Validation

### Automated tests

- **Unit** — AppHost/compose env resolution: missing `TEI_MAX_BATCH_TOKENS` → 1024
- **Integration** — default Aspire stack: TEI args include `--max-batch-tokens 1024`; MCP effective dense cap ≤ 1024

### Success criteria

1. Default Aspire path no longer uses registry 8192 while TEI caps at 1024
2. Operator docs state the pair rule and classify the other TEI WARNs as non-actionable
3. CPU TEI still reaches `Ready` without warmup crash-loop
