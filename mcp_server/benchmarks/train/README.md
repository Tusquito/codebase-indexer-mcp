# Qwen3 code-retrieval fine-tuning (ADR 0020 Phase 1)

Offline maintainer pipeline to fine-tune `Qwen/Qwen3-Embedding-4B` on
query–passage pairs derived from the golden set. Runtime inference stays
Ollama-only; training uses HuggingFace + optional `[train]` dependencies.

## Prerequisites

1. Indexed `codebase-indexer-mcp` collection on Qdrant (same corpus as eval).
2. Ollama serving **base** `qwen3-embedding:4b` for hard-negative mining.
3. CUDA GPU (≥16 GB VRAM recommended for 4B LoRA).
4. Install training extras:

```bash
cd mcp_server
uv sync --extra train
```

## Workflow

### 1. Validate golden labels

Ensure every alias resolves against the indexed collection:

```bash
uv run python -m benchmarks.eval_retrieval --validate-labels
```

### 2. Export query–positive pairs

Scrolls Qdrant for labeled chunk content and writes JSONL:

```bash
uv run python -m benchmarks.train.export_golden_pairs \
  --output benchmarks/train/outputs/golden_pairs.jsonl \
  --validate-labels
```

### 3. Mine hard negatives

Runs base Qwen3 hybrid search; top-k misses become negatives:

```bash
uv run python -m benchmarks.train.mine_hard_negatives \
  --input benchmarks/train/outputs/golden_pairs.jsonl \
  --output benchmarks/train/outputs/pairs_with_negatives.jsonl \
  --top-k 10
```

Mine from **base** Qwen3 only — not a prior fine-tuned checkpoint.

### 4. LoRA fine-tune

Default holdout: all four `multi_hop` golden queries. Best checkpoint saved
by validation MRR under `outputs/checkpoints/best/`:

```bash
uv run python -m benchmarks.train.finetune_qwen3_code \
  --pairs benchmarks/train/outputs/pairs_with_negatives.jsonl \
  --output-dir benchmarks/train/outputs/checkpoints \
  --epochs 3
```

Use `--qlora` when VRAM is tight. Align `--max-seq-length` with
`MAX_DENSE_EMBED_TOKENS` when set in `.env`.

## Training data schema

Each JSONL row:

```json
{
  "query_id": "q_embedder_class",
  "query": "class Embedder embedder.py dense sparse hybrid",
  "positive": "<chunk text from labeled chunk_id>",
  "negatives": ["<hard negative passage>"],
  "tags": ["symbol"]
}
```

## Outputs (gitignored)

| Path | Contents |
|------|----------|
| `outputs/golden_pairs.jsonl` | Exported positives |
| `outputs/pairs_with_negatives.jsonl` | Pairs + mined negatives |
| `outputs/checkpoints/best/` | Best LoRA adapter |
| `outputs/checkpoints/train_summary.json` | Hyperparams + val MRR |

## Next phases (not in Phase 1)

- **Phase 2:** merge LoRA → Ollama Modelfile / registry entry
- **Phase 3:** re-index + `eval_retrieval` vs `eval_baseline_jina.json` gate
- **Phase 4:** optional CI observation job

See [ADR 0020](../../docs/adr/0020-qwen3-code-finetune-jina-quality-gate.md).
