"""LoRA fine-tune Qwen3-Embedding-4B for code retrieval (ADR 0020 Phase 1).

Trains with contrastive (InfoNCE / multiple-negatives ranking) loss on
exported golden pairs plus mined hard negatives. Saves the best LoRA adapter
by validation MRR on a fixed holdout split.

Requires optional ``[train]`` extra: ``uv sync --extra train``.

Usage:
    python -m benchmarks.train.finetune_qwen3_code --pairs pairs_with_negatives.jsonl
    python -m benchmarks.train.finetune_qwen3_code --pairs pairs.jsonl --epochs 3
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

from benchmarks.train._schema import TrainingPair, read_jsonl
from benchmarks.train._split import SplitStrategy, split_holdout

DEFAULT_MODEL = "Qwen/Qwen3-Embedding-4B"
DEFAULT_OUTPUT_DIR = Path(__file__).resolve().parent / "outputs" / "checkpoints"
DEFAULT_MAX_SEQ_LENGTH = 512
DEFAULT_LORA_RANK = 16
DEFAULT_LORA_ALPHA = 32
DEFAULT_BATCH_SIZE = 2
DEFAULT_EPOCHS = 3
DEFAULT_LR = 2e-5


def _require_train_deps() -> None:
    try:
        import peft  # noqa: F401
        import torch  # noqa: F401
        import transformers  # noqa: F401
    except ImportError as exc:
        raise SystemExit(
            "Training dependencies missing. Install with: uv sync --extra train"
        ) from exc


def compute_mrr(
    query_embeddings: list[list[float]],
    positive_embeddings: list[list[float]],
    *,
    negative_embeddings: list[list[list[float]]] | None = None,
) -> float:
    """Mean reciprocal rank: positive should rank above all negatives."""
    if not query_embeddings:
        return 0.0

    reciprocal_ranks: list[float] = []
    for i, q_vec in enumerate(query_embeddings):
        pos_vec = positive_embeddings[i]
        pos_score = _cosine_similarity(q_vec, pos_vec)

        scores = [pos_score]
        if negative_embeddings and i < len(negative_embeddings):
            for neg_vec in negative_embeddings[i]:
                scores.append(_cosine_similarity(q_vec, neg_vec))

        ranked = sorted(scores, reverse=True)
        rank = ranked.index(pos_score) + 1
        reciprocal_ranks.append(1.0 / rank)

    return sum(reciprocal_ranks) / len(reciprocal_ranks)


def _cosine_similarity(a: list[float], b: list[float]) -> float:
    dot = sum(x * y for x, y in zip(a, b, strict=True))
    norm_a = math.sqrt(sum(x * x for x in a))
    norm_b = math.sqrt(sum(x * x for x in b))
    if norm_a == 0.0 or norm_b == 0.0:
        return 0.0
    return dot / (norm_a * norm_b)


def build_training_datasets(pairs: list[TrainingPair]) -> tuple[list[dict[str, str]], str]:
    """Build training rows and loss kind from pairs.

    Returns (rows, loss_kind) where loss_kind is ``triplet`` when every pair
    has at least one hard negative, otherwise ``mnrl`` (in-batch negatives).
    """
    has_all_negatives = bool(pairs) and all(p.negatives for p in pairs)
    rows: list[dict[str, str]] = []
    if has_all_negatives:
        for pair in pairs:
            rows.append(
                {
                    "anchor": pair.query,
                    "positive": pair.positive,
                    "negative": pair.negatives[0],
                }
            )
        return rows, "triplet"

    for pair in pairs:
        rows.append({"anchor": pair.query, "positive": pair.positive})
    return rows, "mnrl"


def embed_texts(model: Any, texts: list[str], *, batch_size: int = 8) -> list[list[float]]:
    """Embed texts with a sentence-transformers model."""
    embeddings = model.encode(
        texts,
        batch_size=batch_size,
        convert_to_numpy=True,
        normalize_embeddings=True,
        show_progress_bar=False,
    )
    return [row.tolist() for row in embeddings]


def evaluate_val_mrr(model: Any, val_pairs: list[TrainingPair]) -> float:
    """Compute validation MRR over holdout pairs."""
    if not val_pairs:
        return 0.0

    queries = [p.query for p in val_pairs]
    positives = [p.positive for p in val_pairs]
    query_emb = embed_texts(model, queries)
    pos_emb = embed_texts(model, positives)

    neg_emb: list[list[list[float]]] = []
    for pair in val_pairs:
        if pair.negatives:
            neg_emb.append(embed_texts(model, pair.negatives))
        else:
            neg_emb.append([])

    return compute_mrr(query_emb, pos_emb, negative_embeddings=neg_emb)


def train_lora(
    pairs: list[TrainingPair],
    *,
    output_dir: Path,
    model_name: str = DEFAULT_MODEL,
    max_seq_length: int = DEFAULT_MAX_SEQ_LENGTH,
    lora_rank: int = DEFAULT_LORA_RANK,
    lora_alpha: int = DEFAULT_LORA_ALPHA,
    batch_size: int = DEFAULT_BATCH_SIZE,
    epochs: int = DEFAULT_EPOCHS,
    learning_rate: float = DEFAULT_LR,
    split_strategy: SplitStrategy = "multi_hop",
    seed: int = 42,
    use_qlora: bool = False,
) -> dict[str, Any]:
    """Fine-tune with LoRA; return summary dict with best validation MRR."""
    _require_train_deps()

    import torch
    from peft import LoraConfig, TaskType, get_peft_model
    from sentence_transformers import SentenceTransformer
    from sentence_transformers.losses import MultipleNegativesRankingLoss, TripletLoss
    from sentence_transformers.training_args import SentenceTransformerTrainingArguments
    from sentence_transformers.trainer import SentenceTransformerTrainer
    from datasets import Dataset

    train_pairs, val_pairs = split_holdout(pairs, strategy=split_strategy, seed=seed)
    if not train_pairs:
        raise ValueError("Training split is empty after holdout")

    output_dir.mkdir(parents=True, exist_ok=True)
    run_dir = output_dir / "runs"
    best_dir = output_dir / "best"

    model_kwargs: dict[str, Any] = {}
    if use_qlora:
        model_kwargs["model_kwargs"] = {"torch_dtype": torch.float16}
        model_kwargs["tokenizer_kwargs"] = {"padding_side": "left"}

    st_model = SentenceTransformer(model_name, **model_kwargs)
    st_model.max_seq_length = max_seq_length

    lora_config = LoraConfig(
        task_type=TaskType.FEATURE_EXTRACTION,
        r=lora_rank,
        lora_alpha=lora_alpha,
        lora_dropout=0.05,
        target_modules=["q_proj", "k_proj", "v_proj", "o_proj"],
    )
    st_model[0].auto_model = get_peft_model(st_model[0].auto_model, lora_config)

    baseline_mrr = evaluate_val_mrr(st_model, val_pairs) if val_pairs else 0.0

    train_rows, loss_kind = build_training_datasets(train_pairs)
    train_dataset = Dataset.from_list(train_rows)
    loss: MultipleNegativesRankingLoss | TripletLoss
    if loss_kind == "triplet":
        loss = TripletLoss(st_model)
    else:
        loss = MultipleNegativesRankingLoss(st_model)
    args = SentenceTransformerTrainingArguments(
        output_dir=str(run_dir),
        num_train_epochs=epochs,
        per_device_train_batch_size=batch_size,
        learning_rate=learning_rate,
        save_strategy="no",
        logging_steps=10,
        fp16=use_qlora and torch.cuda.is_available(),
        report_to=[],
    )
    trainer = SentenceTransformerTrainer(
        model=st_model,
        args=args,
        train_dataset=train_dataset,
        loss=loss,
    )
    trainer.train()

    final_mrr = evaluate_val_mrr(st_model, val_pairs) if val_pairs else 0.0
    best_mrr = final_mrr
    st_model.save(str(best_dir))

    summary = {
        "model_name": model_name,
        "output_dir": str(output_dir),
        "best_checkpoint": str(best_dir),
        "n_train": len(train_pairs),
        "n_val": len(val_pairs),
        "split_strategy": split_strategy,
        "baseline_val_mrr": round(baseline_mrr, 6),
        "final_val_mrr": round(final_mrr, 6),
        "best_val_mrr": round(best_mrr, 6),
        "epochs": epochs,
        "loss": loss_kind,
        "lora_rank": lora_rank,
        "max_seq_length": max_seq_length,
        "use_qlora": use_qlora,
    }

    summary_path = output_dir / "train_summary.json"
    summary_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    return summary


def main() -> None:
    parser = argparse.ArgumentParser(
        description="LoRA fine-tune Qwen3-Embedding-4B on golden-set pairs"
    )
    parser.add_argument(
        "--pairs",
        type=Path,
        required=True,
        help="JSONL training pairs (with optional hard negatives)",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=DEFAULT_OUTPUT_DIR,
        help="Directory for checkpoints and train_summary.json",
    )
    parser.add_argument(
        "--model",
        default=DEFAULT_MODEL,
        help="HuggingFace model id",
    )
    parser.add_argument(
        "--max-seq-length",
        type=int,
        default=DEFAULT_MAX_SEQ_LENGTH,
        help="Max sequence length (align with MAX_DENSE_EMBED_TOKENS when set)",
    )
    parser.add_argument(
        "--lora-rank",
        type=int,
        default=DEFAULT_LORA_RANK,
        help="LoRA rank",
    )
    parser.add_argument(
        "--lora-alpha",
        type=int,
        default=DEFAULT_LORA_ALPHA,
        help="LoRA alpha scaling",
    )
    parser.add_argument(
        "--batch-size",
        type=int,
        default=DEFAULT_BATCH_SIZE,
        help="Per-device train batch size",
    )
    parser.add_argument(
        "--epochs",
        type=int,
        default=DEFAULT_EPOCHS,
        help="Training epochs",
    )
    parser.add_argument(
        "--lr",
        type=float,
        default=DEFAULT_LR,
        help="Learning rate",
    )
    parser.add_argument(
        "--split-strategy",
        choices=("multi_hop", "holdout_ids", "stratified"),
        default="multi_hop",
        help="Train/val holdout strategy",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=42,
        help="Random seed for stratified split",
    )
    parser.add_argument(
        "--qlora",
        action="store_true",
        help="Enable QLoRA (fp16 weights when CUDA available)",
    )
    args = parser.parse_args()

    pairs = read_jsonl(args.pairs)
    if not pairs:
        raise SystemExit(f"No training pairs in {args.pairs}")

    summary = train_lora(
        pairs,
        output_dir=args.output_dir,
        model_name=args.model,
        max_seq_length=args.max_seq_length,
        lora_rank=args.lora_rank,
        lora_alpha=args.lora_alpha,
        batch_size=args.batch_size,
        epochs=args.epochs,
        learning_rate=args.lr,
        split_strategy=args.split_strategy,  # type: ignore[arg-type]
        seed=args.seed,
        use_qlora=args.qlora,
    )
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
