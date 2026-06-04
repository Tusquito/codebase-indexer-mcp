# src/codebase_indexer/indexer/truncation.py
"""Token-aware text truncation for embedding inputs."""

from __future__ import annotations

import json
import logging
from pathlib import Path
from typing import Any, Literal

_log = logging.getLogger(__name__)

TruncationSource = Literal[
    "env_override",
    "model_auto_detect",
    "disabled",
]


def read_model_max_tokens_from_dir(model_dir: Path) -> int | None:
    """Read max sequence length from fastembed model cache files."""
    for name in ("tokenizer_config.json", "config.json"):
        path = model_dir / name
        if not path.is_file():
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        for key in ("model_max_length", "max_position_embeddings", "max_seq_length"):
            val = data.get(key)
            if isinstance(val, int) and val > 0:
                return val
    return None


def resolve_max_embed_tokens(
    *,
    role: str,
    model_name: str,
    env_tokens: int,
    model_dir: Path | None,
    known_registry: dict[str, int],
) -> tuple[int, TruncationSource]:
    """Resolve effective max tokens for dense or sparse embedding input.

    Precedence: explicit env tokens > auto-detect from model.
    Returns (0, disabled) when no limit applies (sparse BM25 with tokens=0).
    """
    if env_tokens > 0:
        _log.info(
            "truncation_strategy %s_tokens=%d source=env_override model=%s",
            role,
            env_tokens,
            model_name,
        )
        return env_tokens, "env_override"

    detected: int | None = None
    if model_dir is not None:
        detected = read_model_max_tokens_from_dir(model_dir)
    if detected is None:
        detected = known_registry.get(model_name)
    if detected is not None and detected > 0:
        _log.info(
            "truncation_strategy %s_tokens=%d source=model_auto_detect model=%s",
            role,
            detected,
            model_name,
        )
        return detected, "model_auto_detect"

    if role == "sparse":
        _log.info(
            "truncation_strategy sparse_tokens=0 source=disabled model=%s",
            model_name,
        )
        return 0, "disabled"
    _log.warning(
        "truncation_strategy dense_tokens=unset — no truncation until model is known model=%s",
        model_name,
    )
    return 0, "disabled"


def truncate_with_tokenizer(text: str, tokenizer: Any, max_tokens: int) -> tuple[str, int]:
    """Truncate text to at most max_tokens using a HuggingFace tokenizers.Tokenizer.

    Returns (truncated_text, token_count). token_count is -1 when unknown (fast path).
    """
    if max_tokens <= 0:
        return text, -1
    # Conservative fast path: very short text cannot exceed max_tokens.
    if len(text) <= max_tokens * 2:
        return text, -1
    encoding = tokenizer.encode(text, add_special_tokens=False)
    token_count = len(encoding.ids)
    if token_count <= max_tokens:
        return text, token_count
    offsets = encoding.offsets
    if offsets and len(offsets) >= max_tokens:
        char_end = offsets[max_tokens - 1][1]
        return text[:char_end], max_tokens
    return tokenizer.decode(encoding.ids[:max_tokens]), max_tokens


def truncate_bm25_text(text: str, max_tokens: int) -> tuple[str, int]:
    """Truncate for BM25-style tokenizers (word split, no char offsets)."""
    if max_tokens <= 0:
        return text, -1
    from fastembed.sparse.utils.tokenizer import SimpleTokenizer

    tokens = SimpleTokenizer.tokenize(text)
    token_count = len(tokens)
    if token_count <= max_tokens:
        return text, token_count
    return " ".join(tokens[:max_tokens]), max_tokens


def truncate_for_embedding(
    text: str,
    *,
    max_tokens: int,
    tokenizer: Any | None,
) -> tuple[str, int]:
    """Truncate a single string for embedding.

    Returns (truncated_text, token_count). token_count is -1 when unknown.
    """
    if max_tokens <= 0:
        return text, -1
    if tokenizer is None:
        return text, -1
    # BM25 exposes SimpleTokenizer as a class with static tokenize, not HF Tokenizer.
    if isinstance(tokenizer, type):
        return truncate_bm25_text(text, max_tokens)
    return truncate_with_tokenizer(text, tokenizer, max_tokens)
