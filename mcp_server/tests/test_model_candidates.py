"""Registry schema + config-consistency tests for ADR 0026 candidates."""

import pytest

from benchmarks.candidates import (
    INCLUDED_STATUSES,
    RegistryError,
    load_registry,
)


@pytest.fixture(scope="module")
def registry():
    return load_registry()


def test_registry_loads_all_ten_rows(registry):
    assert len(registry) == 10


def test_golden_set_version_present(registry):
    assert registry.golden_set_version == "v4-expanded-75q"


def test_exactly_one_default_role(registry):
    defaults = [c for c in registry if c.role == "default"]
    assert len(defaults) == 1
    assert defaults[0].model == "jinaai/jina-embeddings-v2-base-code"


def test_gated_embeddinggemma_excluded_with_rationale(registry):
    gemma = registry.by_model("google/embeddinggemma-300m")
    assert gemma.status == "excluded"
    assert gemma.rationale
    assert not gemma.included


def test_granite_97m_included(registry):
    cand = registry.by_model("ibm-granite/granite-embedding-97m-multilingual-r2")
    assert cand.included
    assert cand.vector_size == 384


def test_inf_retriever_spike_passed(registry):
    cand = registry.by_model("infly/inf-retriever-v1-1.5b")
    assert cand.status == "spike_passed"
    assert cand.spike == "instruction_prefix"
    assert cand.included


def test_pplx_embed_dropped_with_rationale(registry):
    for model in ("perplexity-ai/pplx-embed-v1-0.6b", "perplexity-ai/pplx-embed-v1-4b"):
        cand = registry.by_model(model)
        assert cand.status == "dropped"
        assert cand.rationale
        assert not cand.included


def test_included_property_matches_statuses(registry):
    for cand in registry:
        assert cand.included == (cand.status in INCLUDED_STATUSES)


def test_included_pool_is_seven(registry):
    # 6 native (jina, qwen 0.6b/4b, gte, granite 311m/97m) + inf-retriever spike.
    assert len(registry.included) == 7


def test_native_included_candidates_are_config_wired(registry):
    from codebase_indexer.config import (
        KNOWN_EMBED_MODEL_DIMENSIONS,
        QWEN3_EMBED_SPECS,
    )

    for cand in registry.included:
        if cand.tei_status != "native":
            continue
        if cand.model in QWEN3_EMBED_SPECS:
            continue
        assert cand.model in KNOWN_EMBED_MODEL_DIMENSIONS


def test_dropped_without_rationale_rejected(tmp_path):
    bad = tmp_path / "bad.yaml"
    bad.write_text(
        "golden_set_version: x\n"
        "candidates:\n"
        "  - model: foo/bar\n"
        "    params: '1M'\n"
        "    native_dim: 768\n"
        "    vector_size: 768\n"
        "    context: 512\n"
        "    license: MIT\n"
        "    tei_status: native\n"
        "    status: dropped\n",
        encoding="utf-8",
    )
    with pytest.raises(RegistryError, match="rationale"):
        load_registry(bad)


def test_spike_without_spike_name_rejected(tmp_path):
    bad = tmp_path / "bad.yaml"
    bad.write_text(
        "golden_set_version: x\n"
        "candidates:\n"
        "  - model: foo/bar\n"
        "    params: '1M'\n"
        "    native_dim: 768\n"
        "    vector_size: 768\n"
        "    context: 512\n"
        "    license: MIT\n"
        "    tei_status: spike\n"
        "    status: spike_passed\n",
        encoding="utf-8",
    )
    with pytest.raises(RegistryError, match="spike"):
        load_registry(bad)


def test_native_dim_mismatch_with_config_rejected(tmp_path):
    bad = tmp_path / "bad.yaml"
    bad.write_text(
        "golden_set_version: x\n"
        "candidates:\n"
        "  - model: Alibaba-NLP/gte-modernbert-base\n"
        "    params: '149M'\n"
        "    native_dim: 512\n"
        "    vector_size: 512\n"
        "    context: 8192\n"
        "    license: Apache-2.0\n"
        "    tei_status: native\n"
        "    status: registered\n",
        encoding="utf-8",
    )
    with pytest.raises(RegistryError, match="native_dim"):
        load_registry(bad)


def test_duplicate_model_rejected(tmp_path):
    bad = tmp_path / "bad.yaml"
    row = (
        "  - model: nomic-ai/nomic-embed-text-v1.5\n"
        "    params: '1M'\n"
        "    native_dim: 768\n"
        "    vector_size: 768\n"
        "    context: 8192\n"
        "    license: MIT\n"
        "    tei_status: native\n"
        "    status: registered\n"
    )
    bad.write_text("golden_set_version: x\ncandidates:\n" + row + row, encoding="utf-8")
    with pytest.raises(RegistryError, match="duplicate"):
        load_registry(bad)
