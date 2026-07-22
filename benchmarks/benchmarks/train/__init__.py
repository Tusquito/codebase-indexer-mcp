"""Offline Qwen3 code-retrieval fine-tuning pipeline (ADR 0020 Phase 1).

Maintainer workflow: export golden pairs → mine hard negatives → LoRA train.
See ``benchmarks/train/README.md`` for step-by-step instructions.
"""
