"""Unit tests for scripts/tune_alloc.py (ADR 0024 Phase 1). No Docker."""

from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from scripts.tune_alloc import (  # noqa: E402
    RAM_FLOORS_GIB,
    Budget,
    FeatureFlags,
    HostResources,
    allocate_cpus,
    allocate_ram,
    build_allocation,
    render_env_fragment,
    resolve_budget,
    resolve_services,
    seed_knobs,
)


def _budget(ram: float, cpus: int) -> Budget:
    return Budget(max_cpus=cpus, max_ram_gib=ram, reserve_gib=2.5)


# --- budget resolution -----------------------------------------------------


def test_budget_defaults_half_ram_all_cpus():
    host = HostResources(cpu_count=16, total_ram_gib=32.0)
    budget = resolve_budget(host)
    assert budget.max_cpus == 16
    assert budget.max_ram_gib == 16  # floor(32/2)
    assert budget.reserve_gib == 2.5
    assert budget.usable_ram_gib == 13.5
    assert budget.usable_cpus == 16


def test_budget_requires_max_ram_when_undetected():
    host = HostResources(cpu_count=8, total_ram_gib=None)
    try:
        resolve_budget(host)
    except ValueError:
        pass
    else:
        raise AssertionError("expected ValueError when RAM undetected")
    budget = resolve_budget(host, max_ram_gib=8)
    assert budget.max_ram_gib == 8


# --- feature-flag / service resolution -------------------------------------


def test_flags_override_env():
    services = resolve_services(
        FeatureFlags(gpu=False, colbert=True, neo4j=True), env={}
    )
    assert services.accelerator == "cpu"
    assert services.colbert is True
    assert services.neo4j is True
    assert services.tei is True  # bundled assumed


def test_env_inherited_when_flags_unset():
    env = {"ACCELERATOR": "cpu", "RERANK_ENABLED": "true", "GRAPH_ENABLED": "true"}
    services = resolve_services(FeatureFlags(), env=env)
    assert services.accelerator == "cpu"
    assert services.colbert is True  # remote backend default
    assert services.neo4j is True


def test_external_tei_excluded_from_caps():
    services = resolve_services(FeatureFlags(), env={"TEI_URL": "http://remote-host:8080"})
    assert services.tei_external is True
    assert services.tei is False
    assert "tei" not in services.active


def test_no_colbert_flag_beats_env():
    env = {"RERANK_ENABLED": "true"}
    services = resolve_services(FeatureFlags(colbert=False), env=env)
    assert services.colbert is False


# --- RAM allocation --------------------------------------------------------


def test_ram_gpu_tei_colbert_respects_mcp_cap_and_budget():
    budget = _budget(ram=16, cpus=16)  # usable 13.5
    services = resolve_services(FeatureFlags(gpu=True, colbert=True, neo4j=False), env={})
    ram = allocate_ram(budget, services)
    assert set(ram) == {"mcp", "qdrant", "tei", "colbert"}
    # MCP capped <=35% of usable and floors respected.
    assert ram["mcp"] <= 0.35 * budget.usable_ram_gib + 1e-9
    assert ram["mcp"] >= RAM_FLOORS_GIB["mcp"]
    assert ram["colbert"] >= RAM_FLOORS_GIB["colbert"]
    assert sum(ram.values()) <= budget.usable_ram_gib + 1e-9


def test_ram_sum_within_budget_after_shrink():
    budget = _budget(ram=12, cpus=8)  # usable 9.5; raw floors overflow -> shrink
    services = resolve_services(FeatureFlags(gpu=True, colbert=True, neo4j=True), env={})
    ram = allocate_ram(budget, services)
    assert sum(ram.values()) <= budget.usable_ram_gib + 1e-9
    for svc, val in ram.items():
        assert val >= RAM_FLOORS_GIB[svc]


def test_ram_floors_win_on_infeasible_tiny_budget():
    budget = _budget(ram=8, cpus=8)  # usable 5.5 < sum of 5-service floors
    services = resolve_services(FeatureFlags(gpu=True, colbert=True, neo4j=True), env={})
    ram = allocate_ram(budget, services)
    for svc, val in ram.items():
        assert val >= RAM_FLOORS_GIB[svc]


def test_ram_cpu_dense_topology_shares():
    budget = _budget(ram=20, cpus=8)  # usable 17.5
    services = resolve_services(
        FeatureFlags(gpu=False, colbert=False, neo4j=False),
        env={"TEI_URL": "http://external:8080"},  # no bundled tei
    )
    ram = allocate_ram(budget, services)
    assert set(ram) == {"mcp", "qdrant"}
    # 55% / 35% of 17.5 rounded to 0.5.
    assert ram["mcp"] == 9.5  # round(0.55*17.5=9.625) -> 9.5
    assert ram["qdrant"] == 6.0  # round(0.35*17.5=6.125) -> 6.0


def test_ram_neo4j_included():
    budget = _budget(ram=24, cpus=16)
    services = resolve_services(FeatureFlags(gpu=True, colbert=False, neo4j=True), env={})
    ram = allocate_ram(budget, services)
    assert "neo4j" in ram
    assert ram["neo4j"] >= RAM_FLOORS_GIB["neo4j"]


# --- CPU allocation --------------------------------------------------------


def test_cpu_slices_and_mcp_remainder():
    budget = _budget(ram=32, cpus=16)
    services = resolve_services(FeatureFlags(gpu=True, colbert=True, neo4j=True), env={})
    cpus = allocate_cpus(budget, services)
    slice_cpus = min(4, max(2, 16 // 8))  # = 2
    assert cpus["qdrant"] == slice_cpus
    assert cpus["tei"] == slice_cpus
    assert cpus["colbert"] == slice_cpus
    assert cpus["neo4j"] == 2
    assert cpus["mcp"] == 16 - (slice_cpus * 3 + 2)
    assert cpus["mcp"] >= 2
    assert sum(cpus.values()) <= budget.usable_cpus


def test_cpu_small_machine_preset():
    budget = _budget(ram=8, cpus=4)
    services = resolve_services(FeatureFlags(gpu=True, colbert=False, neo4j=False), env={})
    cpus = allocate_cpus(budget, services)
    assert cpus["qdrant"] == 1
    assert cpus["tei"] == 1
    assert cpus["mcp"] >= 2


# --- knob seed -------------------------------------------------------------


def test_knob_seed_colbert_bounds():
    knobs = seed_knobs(
        ram={"mcp": 3.0, "qdrant": 2.0},
        cpus={"mcp": 8, "qdrant": 2},
        services=resolve_services(FeatureFlags(colbert=True), env={}),
    )
    assert int(knobs["UPSERT_BATCH"]) <= 25
    assert int(knobs["FLUSH_EVERY"]) <= 256
    assert knobs["SPARSE_THREADS"] == "4"  # mcp_cpus >= 8
    assert int(knobs["OMP_NUM_THREADS"]) == max(2, 8 - 4 - 1)
    assert knobs["SEQUENTIAL_EMBED"] == "true"  # mcp_mem < 4
    assert knobs["MALLOC_ARENA_MAX"] == "2"


def test_knob_seed_dense_large():
    knobs = seed_knobs(
        ram={"mcp": 20.0, "qdrant": 10.0},
        cpus={"mcp": 12, "qdrant": 4},
        services=resolve_services(FeatureFlags(colbert=False), env={}),
    )
    assert knobs["BATCH_SIZE"] == "64"
    assert knobs["VECTORS_ON_DISK"] == "false"
    assert knobs["QUANTIZATION"] == "false"
    assert "SEQUENTIAL_EMBED" not in knobs
    assert int(knobs["UPSERT_BATCH"]) > 25


def test_knob_seed_low_qdrant_on_disk():
    knobs = seed_knobs(
        ram={"mcp": 5.0, "qdrant": 2.0},
        cpus={"mcp": 4, "qdrant": 2},
        services=resolve_services(FeatureFlags(colbert=False), env={}),
    )
    assert knobs["VECTORS_ON_DISK"] == "true"
    assert knobs["QUANTIZATION"] == "true"


# --- env fragment ----------------------------------------------------------


def test_render_env_fragment_includes_caps_and_feature_vars():
    budget = _budget(ram=8, cpus=16)
    services = resolve_services(FeatureFlags(gpu=True, colbert=True, neo4j=False), env={})
    allocation = build_allocation(budget, services)
    knobs = seed_knobs(allocation.ram_gib, allocation.cpus, services)
    fragment = render_env_fragment(allocation, services, knobs)
    values = fragment.values
    assert values["ACCELERATOR"] == "gpu"
    assert values["RERANK_ENABLED"] == "true"
    assert values["COLBERT_EMBED_BACKEND"] == "remote"
    assert values["COMPOSE_PROFILES"] == "bundled-tei"
    assert "MCP_MEM_LIMIT" in values and values["MCP_MEM_LIMIT"].endswith("g")
    assert "MCP_CPUS" in values
    assert "COLBERT_MEM_LIMIT" in values
    assert "TEI_MEM_LIMIT" in values
    assert "OMP_NUM_THREADS" in values
    rendered = fragment.render()
    assert "MCP_MEM_LIMIT=" in rendered
