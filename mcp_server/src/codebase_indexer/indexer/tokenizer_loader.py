"""Load HuggingFace tokenizers for dense embedding truncation."""

from __future__ import annotations

import logging
import os
from pathlib import Path
from typing import Any

_log = logging.getLogger(__name__)


def resolve_hf_cache_dir() -> Path | None:
    """Return HuggingFace Hub cache directory from environment, if configured."""
    if hub_cache := os.environ.get("HF_HUB_CACHE"):
        return Path(hub_cache)
    if hf_home := os.environ.get("HF_HOME"):
        return Path(hf_home) / "hub"
    if transformers_cache := os.environ.get("TRANSFORMERS_CACHE"):
        return Path(transformers_cache)
    return None


def load_dense_tokenizer(model_id: str) -> Any | None:
    """Load a HuggingFace tokenizer for dense truncation.

    Returns None on import failure or download/load errors (caller passes text
    through unchanged).
    """
    try:
        from tokenizers import Tokenizer
    except ImportError:
        _log.warning(
            "dense_tokenizer_unavailable reason=tokenizers_not_installed model=%s",
            model_id,
        )
        return None

    cache_dir = resolve_hf_cache_dir()
    kwargs: dict[str, str] = {}
    if cache_dir is not None:
        kwargs["cache_dir"] = str(cache_dir)

    try:
        tokenizer = Tokenizer.from_pretrained(model_id, **kwargs)
    except Exception as exc:
        _log.warning(
            "dense_tokenizer_load_failed model=%s cache_dir=%s error=%s",
            model_id,
            cache_dir,
            exc,
        )
        return None

    _log.info(
        "dense_tokenizer_loaded model=%s cache_dir=%s",
        model_id,
        cache_dir,
    )
    return tokenizer
