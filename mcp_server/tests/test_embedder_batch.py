"""Unit tests for adaptive dense embedding batch sizing."""

from codebase_indexer.indexer.embedder import Embedder


def _embedder(**overrides) -> Embedder:
    defaults = dict(
        dense_model="BAAI/bge-small-en-v1.5",
        sparse_model="Qdrant/bm25",
        dense_embed_vector_size=384,
        batch_size=128,
        memory_warn_pct=70,
        memory_halt_pct=85,
    )
    defaults.update(overrides)
    return Embedder(**defaults)


# --- _adaptive_batch_size: low pressure (cgroup known, well below warn) ---

def test_low_pressure_no_reduction_below_512():
    e = _embedder()
    assert e._adaptive_batch_size(511, 128, pressure_pct=15.0) == 128
    assert e._adaptive_batch_size(256, 128, pressure_pct=15.0) == 128
    assert e._adaptive_batch_size(100, 128, pressure_pct=15.0) == 128


def test_low_pressure_halves_at_512():
    e = _embedder()
    assert e._adaptive_batch_size(512, 128, pressure_pct=15.0) == 64
    assert e._adaptive_batch_size(1024, 128, pressure_pct=15.0) == 64


# --- _adaptive_batch_size: elevated pressure (>= warn_pct) ---

def test_elevated_pressure_strict_thresholds():
    e = _embedder()
    assert e._adaptive_batch_size(512, 128, pressure_pct=75.0) == 32
    assert e._adaptive_batch_size(300, 128, pressure_pct=75.0) == 64
    assert e._adaptive_batch_size(200, 128, pressure_pct=75.0) == 128


# --- _adaptive_batch_size: exactly at warn boundary ---

def test_at_warn_boundary_uses_strict_thresholds():
    e = _embedder()
    assert e._adaptive_batch_size(512, 128, pressure_pct=70.0) == 32
    assert e._adaptive_batch_size(300, 128, pressure_pct=70.0) == 64


# --- _adaptive_batch_size: unknown pressure (0.0, e.g. non-Linux) ---

def test_unknown_pressure_uses_strict_thresholds():
    e = _embedder()
    assert e._adaptive_batch_size(512, 128, pressure_pct=0.0) == 32
    assert e._adaptive_batch_size(300, 128, pressure_pct=0.0) == 64
    assert e._adaptive_batch_size(200, 128, pressure_pct=0.0) == 128


# --- _adaptive_batch_size: minimum-1 guarantee ---

def test_never_returns_zero():
    e = _embedder()
    assert e._adaptive_batch_size(1024, 1, pressure_pct=0.0) >= 1
    assert e._adaptive_batch_size(1024, 2, pressure_pct=15.0) >= 1
    assert e._adaptive_batch_size(1024, 3, pressure_pct=80.0) >= 1
