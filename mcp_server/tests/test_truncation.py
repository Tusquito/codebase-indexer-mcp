"""Unit tests for token-aware truncation helpers."""

from pathlib import Path

import pytest

from codebase_indexer.indexer.truncation import (
    read_model_max_tokens_from_dir,
    resolve_max_embed_tokens,
    truncate_bm25_text,
    truncate_for_embedding,
    truncate_with_tokenizer,
)


def test_resolve_env_override():
    max_tok, source = resolve_max_embed_tokens(
        role="dense",
        model_name="BAAI/bge-small-en-v1.5",
        env_tokens=256,
        model_dir=None,
        known_registry={"BAAI/bge-small-en-v1.5": 512},
    )
    assert max_tok == 256
    assert source == "env_override"


def test_resolve_auto_detect_from_registry():
    max_tok, source = resolve_max_embed_tokens(
        role="dense",
        model_name="nomic-ai/nomic-embed-text-v1.5",
        env_tokens=0,
        model_dir=None,
        known_registry={"nomic-ai/nomic-embed-text-v1.5": 8192},
    )
    assert max_tok == 8192
    assert source == "model_auto_detect"


def test_resolve_sparse_disabled_for_unknown():
    max_tok, source = resolve_max_embed_tokens(
        role="sparse",
        model_name="Qdrant/bm25",
        env_tokens=0,
        model_dir=None,
        known_registry={},
    )
    assert max_tok == 0
    assert source == "disabled"


def test_read_model_max_tokens_from_dir(tmp_path: Path):
    (tmp_path / "tokenizer_config.json").write_text(
        '{"model_max_length": 512}', encoding="utf-8"
    )
    assert read_model_max_tokens_from_dir(tmp_path) == 512


@pytest.mark.parametrize(
    "text,max_tokens,expect_shorter",
    [
        ("short", 512, False),
        ("word " * 2000, 10, True),
    ],
)
def test_truncate_with_tokenizer_needs_tokenizers(text, max_tokens, expect_shorter):
    pytest.importorskip("tokenizers")
    from tokenizers import Tokenizer

    tok = Tokenizer.from_pretrained("bert-base-uncased")
    result, count = truncate_with_tokenizer(text, tok, max_tokens)
    if expect_shorter:
        assert len(result) < len(text)
        assert count > 0
    else:
        assert result == text


def test_truncate_for_embedding_noop_when_zero():
    result, count = truncate_for_embedding("hello", max_tokens=0, tokenizer=None)
    assert result == "hello"
    assert count == -1


def test_truncate_bm25_text_truncates():
    pytest.importorskip("fastembed")
    text = "one two three four five six"
    result, count = truncate_bm25_text(text, max_tokens=3)
    assert result == "one two three"
    assert count == 3


def test_truncate_for_embedding_bm25_dispatch():
    pytest.importorskip("fastembed")
    from fastembed.sparse.utils.tokenizer import SimpleTokenizer

    text = "alpha beta gamma delta"
    result, count = truncate_for_embedding(
        text, max_tokens=2, tokenizer=SimpleTokenizer
    )
    assert result == "alpha beta"
    assert count == 2
