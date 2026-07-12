#!/usr/bin/env python3
"""Pure resource allocation and knob-seed logic for the stack tuner (ADR 0024, Phase 1).

This module is intentionally free of argparse, Docker, and any file writes so the
allocation math can be unit-tested in isolation. It mirrors the feature-flag
precedence used by :mod:`scripts.compose_files` (explicit flag -> env -> default).

Allocation tables (Phase B RAM shares, Phase C CPU slices, Phase D knob seeds)
are transcribed from ``docs/adr/0024-resource-aware-stack-tuner.md``.
"""

from __future__ import annotations

import os
import subprocess
import sys
from collections.abc import Mapping
from dataclasses import dataclass, field
from pathlib import Path
from urllib.parse import urlsplit

REPO_ROOT = Path(__file__).resolve().parents[1]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.accelerator import get_accelerator  # noqa: E402

DEFAULT_RESERVE_GIB = 2.5
DARWIN_RESERVE_GIB = 4.0


def default_reserve_gib() -> float:
    """Kernel/Docker headroom: 4 GiB on macOS, 2.5 GiB elsewhere."""
    if sys.platform == "darwin":
        return DARWIN_RESERVE_GIB
    return DEFAULT_RESERVE_GIB

# Phase B minimum RSS floors (GiB) per service.
RAM_FLOORS_GIB: dict[str, float] = {
    "mcp": 2.0,
    "qdrant": 1.5,
    "tei": 2.0,
    "colbert": 2.0,
    "neo4j": 1.0,
}

# Phase B RAM share tables (percent of usable_ram_gib) keyed by topology.
# neo4j share is applied only when the graph service is active.
_RAM_SHARES: dict[str, dict[str, float]] = {
    # CPU dense, no bundled TEI in cap set.
    "cpu_dense": {"mcp": 55, "qdrant": 35, "neo4j": 10},
    # GPU TEI sidecar, no ColBERT rerank.
    "gpu_tei": {"mcp": 30, "qdrant": 40, "tei": 15, "neo4j": 15},
    # GPU TEI + ColBERT rerank.
    "gpu_tei_colbert": {"mcp": 25, "qdrant": 30, "tei": 15, "colbert": 20, "neo4j": 10},
    # Graph-focused host with external TEI (TEI excluded from cap set).
    "graph_only": {"mcp": 45, "qdrant": 35, "neo4j": 20},
}

COLBERT_MCP_CAP_PCT = 35.0  # MCP capped at <=35% of usable RAM when rerank active.


def _truthy(value: str | None) -> bool:
    if value is None:
        return False
    return value.strip().lower() in ("1", "true", "yes", "on")


@dataclass(frozen=True)
class HostResources:
    """Detected (or declared) host capacity."""

    cpu_count: int
    total_ram_gib: float | None  # None when RAM could not be detected


@dataclass(frozen=True)
class Budget:
    """Resolved budget the stack is allowed to consume."""

    max_cpus: int
    max_ram_gib: float
    reserve_gib: float = field(default_factory=default_reserve_gib)

    @property
    def usable_ram_gib(self) -> float:
        return max(0.0, self.max_ram_gib - self.reserve_gib)

    @property
    def usable_cpus(self) -> int:
        return max(1, self.max_cpus)


@dataclass(frozen=True)
class FeatureFlags:
    """Tri-state CLI feature flags (None = unset, defer to env/default)."""

    gpu: bool | None = None  # True=--gpu, False=--cpu, None=inherit env
    colbert: bool | None = None
    neo4j: bool | None = None


@dataclass(frozen=True)
class ServiceSet:
    """Resolved active services and accelerator for the target topology."""

    accelerator: str  # "gpu" | "cpu"
    tei: bool
    colbert: bool
    neo4j: bool
    tei_external: bool = False

    @property
    def active(self) -> list[str]:
        names = ["mcp", "qdrant"]
        if self.tei:
            names.append("tei")
        if self.colbert:
            names.append("colbert")
        if self.neo4j:
            names.append("neo4j")
        return names


@dataclass(frozen=True)
class Allocation:
    """Per-service RAM (GiB) and CPU (cores) caps."""

    ram_gib: dict[str, float]
    cpus: dict[str, int]


@dataclass
class EnvFragment:
    """Ordered env var mapping destined for stdout or ``.env.tuned``."""

    values: dict[str, str] = field(default_factory=dict)

    def render(self) -> str:
        return "\n".join(f"{k}={v}" for k, v in self.values.items())


# ---------------------------------------------------------------------------
# Host detection
# ---------------------------------------------------------------------------


def _detect_total_ram_gib_darwin() -> float | None:
    try:
        proc = subprocess.run(
            ["sysctl", "-n", "hw.memsize"],
            capture_output=True,
            text=True,
            timeout=5,
            check=False,
        )
        if proc.returncode == 0:
            return int(proc.stdout.strip()) / (1024**3)
    except (OSError, ValueError, subprocess.TimeoutExpired):
        return None
    return None


def _detect_total_ram_gib() -> float | None:
    """Best-effort stdlib RAM detection; None when undetectable.

    On Linux reads ``/proc/meminfo``; on Windows uses ``ctypes`` GlobalMemoryStatusEx;
    on darwin reads ``sysctl hw.memsize``.
    """
    if sys.platform == "darwin":
        return _detect_total_ram_gib_darwin()
    meminfo = Path("/proc/meminfo")
    if meminfo.exists():
        try:
            for line in meminfo.read_text(encoding="utf-8").splitlines():
                if line.startswith("MemTotal:"):
                    kib = float(line.split()[1])
                    return kib / (1024 * 1024)
        except (OSError, ValueError, IndexError):
            return None
    if sys.platform.startswith("win"):
        try:
            import ctypes

            class _MemoryStatusEx(ctypes.Structure):
                _fields_ = [
                    ("dwLength", ctypes.c_ulong),
                    ("dwMemoryLoad", ctypes.c_ulong),
                    ("ullTotalPhys", ctypes.c_ulonglong),
                    ("ullAvailPhys", ctypes.c_ulonglong),
                    ("ullTotalPageFile", ctypes.c_ulonglong),
                    ("ullAvailPageFile", ctypes.c_ulonglong),
                    ("ullTotalVirtual", ctypes.c_ulonglong),
                    ("ullAvailVirtual", ctypes.c_ulonglong),
                    ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
                ]

            stat = _MemoryStatusEx()
            stat.dwLength = ctypes.sizeof(_MemoryStatusEx)
            if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(stat)):
                return stat.ullTotalPhys / (1024**3)
        except (OSError, AttributeError, ValueError):
            return None
    return None


def detect_host() -> HostResources:
    """Detect logical CPU count and total RAM (GiB), tolerating missing RAM."""
    return HostResources(
        cpu_count=os.cpu_count() or 1,
        total_ram_gib=_detect_total_ram_gib(),
    )


def resolve_budget(
    host: HostResources,
    *,
    max_cpus: int | None = None,
    max_ram_gib: float | None = None,
    reserve_gib: float | None = None,
) -> Budget:
    """Apply ADR defaults: max-cpus=host, max-ram-gib=floor(host/2), reserve from host OS.

    Raises ``ValueError`` when RAM is neither detected nor supplied.
    """
    cpus = max_cpus if max_cpus is not None else host.cpu_count
    if max_ram_gib is not None:
        ram = float(max_ram_gib)
    elif host.total_ram_gib is not None:
        ram = float(int(host.total_ram_gib / 2))  # floor(host/2)
    else:
        raise ValueError(
            "Host RAM could not be detected; pass --max-ram-gib to set the budget."
        )
    reserve = default_reserve_gib() if reserve_gib is None else reserve_gib
    return Budget(max_cpus=cpus, max_ram_gib=ram, reserve_gib=reserve)


# ---------------------------------------------------------------------------
# Feature-flag / service resolution (mirrors compose_files.py precedence)
# ---------------------------------------------------------------------------


def _tei_is_external(env: Mapping[str, str]) -> bool:
    url = env.get("TEI_URL")
    if not url or not url.strip():
        return False
    host = (urlsplit(url.strip()).hostname or "").lower()
    return host not in ("", "tei", "localhost", "127.0.0.1")


def resolve_services(flags: FeatureFlags, env: Mapping[str, str] | None = None) -> ServiceSet:
    """Resolve active services from CLI flags, env, and defaults.

    Precedence: explicit CLI flag -> existing env -> script default.
    """
    source = env if env is not None else os.environ

    if flags.gpu is True:
        accelerator = "gpu"
    elif flags.gpu is False:
        accelerator = "cpu"
    else:
        accelerator = get_accelerator(source)

    if flags.colbert is True:
        colbert = True
    elif flags.colbert is False:
        colbert = False
    else:
        backend = (source.get("COLBERT_EMBED_BACKEND") or "").strip().lower()
        colbert = _truthy(source.get("RERANK_ENABLED")) and backend in ("", "remote")

    if flags.neo4j is True:
        neo4j = True
    elif flags.neo4j is False:
        neo4j = False
    else:
        neo4j = _truthy(source.get("GRAPH_ENABLED"))

    tei_external = _tei_is_external(source)
    tei = not tei_external  # bundled TEI assumed unless external host configured

    return ServiceSet(
        accelerator=accelerator,
        tei=tei,
        colbert=colbert,
        neo4j=neo4j,
        tei_external=tei_external,
    )


# ---------------------------------------------------------------------------
# Phase B — RAM allocation
# ---------------------------------------------------------------------------


def _select_ram_topology(services: ServiceSet) -> str:
    if services.tei and services.colbert:
        return "gpu_tei_colbert"
    if services.tei:
        return "gpu_tei"
    if services.neo4j:
        return "graph_only"
    return "cpu_dense"


def _round_half(value: float) -> float:
    """Round to the nearest 0.5 GiB."""
    return round(value * 2) / 2


def _fmt_gib(value: float) -> str:
    if value == int(value):
        return f"{int(value)}g"
    return f"{value:g}g"


def allocate_ram(budget: Budget, services: ServiceSet) -> dict[str, float]:
    """Compute per-service ``*_MEM_LIMIT`` (GiB) per Phase B tables."""
    usable = budget.usable_ram_gib
    shares = _RAM_SHARES[_select_ram_topology(services)]

    raw: dict[str, float] = {}
    for svc in services.active:
        pct = shares.get(svc, 0.0)
        raw[svc] = usable * (pct / 100.0)

    # ColBERT rerank override: MCP multivector flush pressure dominates.
    if services.colbert:
        cap = usable * (COLBERT_MCP_CAP_PCT / 100.0)
        if raw.get("mcp", 0.0) > cap:
            raw["mcp"] = cap

    # Apply per-service floors.
    for svc in raw:
        raw[svc] = max(raw[svc], RAM_FLOORS_GIB[svc])

    # Round to nearest 0.5 GiB.
    alloc = {svc: _round_half(val) for svc, val in raw.items()}

    # Shrink Qdrant then MCP proportionally if over budget (never below floors).
    excess = sum(alloc.values()) - usable
    for svc in ("qdrant", "mcp"):
        if excess <= 0:
            break
        if svc not in alloc:
            continue
        reducible = alloc[svc] - RAM_FLOORS_GIB[svc]
        take = min(excess, max(0.0, reducible))
        alloc[svc] = _round_half(alloc[svc] - take)
        excess = sum(alloc.values()) - usable

    return alloc


# ---------------------------------------------------------------------------
# Phase C — CPU allocation
# ---------------------------------------------------------------------------


def allocate_cpus(budget: Budget, services: ServiceSet) -> dict[str, int]:
    """Compute per-service ``*_CPUS`` (integer cores) per Phase C table."""
    usable = budget.usable_cpus
    alloc: dict[str, int] = {}

    if usable <= 4:
        # Small-machine hard template (mirrors .env.example low-memory presets).
        for svc in ("qdrant", "tei", "colbert", "neo4j"):
            if svc in services.active:
                alloc[svc] = 1
        alloc["mcp"] = max(2, usable - sum(alloc.values()))
        return alloc

    slice_cpus = min(4, max(2, usable // 8))
    if services.tei:
        alloc["tei"] = slice_cpus
    alloc["qdrant"] = slice_cpus
    if services.colbert:
        alloc["colbert"] = slice_cpus
    if services.neo4j:
        alloc["neo4j"] = 2

    alloc["mcp"] = max(2, usable - sum(alloc.values()))
    return alloc


# ---------------------------------------------------------------------------
# Phase D — derived knob seed
# ---------------------------------------------------------------------------


def seed_knobs(
    ram: dict[str, float], cpus: dict[str, int], services: ServiceSet
) -> dict[str, str]:
    """Derive initial pipeline knobs from allocation + topology (Phase D)."""
    mcp_cpus = cpus.get("mcp", 2)
    mcp_mem = ram.get("mcp", RAM_FLOORS_GIB["mcp"])
    qdrant_mem = ram.get("qdrant", RAM_FLOORS_GIB["qdrant"])

    sparse_threads = 4 if mcp_cpus >= 8 else 2
    omp_threads = max(2, mcp_cpus - sparse_threads - 1)

    if mcp_mem <= 8:
        batch_size = 16
    elif mcp_mem <= 16:
        batch_size = 32
    else:
        batch_size = 64

    if services.colbert:
        flush_every = 96
        upsert_batch = 10
    else:
        flush_every = 750 if mcp_mem <= 8 else 1500
        if mcp_mem > 16:
            upsert_batch = 500
        elif mcp_mem > 8:
            upsert_batch = 200
        else:
            upsert_batch = 50

    knobs: dict[str, str] = {
        "OMP_NUM_THREADS": str(omp_threads),
        "SPARSE_THREADS": str(sparse_threads),
        "BATCH_SIZE": str(batch_size),
        "FLUSH_EVERY": str(flush_every),
        "UPSERT_BATCH": str(upsert_batch),
    }

    # Qdrant storage knobs.
    if qdrant_mem < 4:
        knobs["VECTORS_ON_DISK"] = "true"
        knobs["QUANTIZATION"] = "true"
    elif qdrant_mem >= 8:
        knobs["VECTORS_ON_DISK"] = "false"
        knobs["QUANTIZATION"] = "false"

    if mcp_mem < 4:
        knobs["SEQUENTIAL_EMBED"] = "true"
    if mcp_mem < 6:
        knobs["MALLOC_ARENA_MAX"] = "2"

    return knobs


# ---------------------------------------------------------------------------
# Env fragment rendering
# ---------------------------------------------------------------------------

_CAP_ENV_NAMES: dict[str, tuple[str, str]] = {
    "mcp": ("MCP_MEM_LIMIT", "MCP_CPUS"),
    "qdrant": ("QDRANT_MEM_LIMIT", "QDRANT_CPUS"),
    "tei": ("TEI_MEM_LIMIT", "TEI_CPUS"),
    "colbert": ("COLBERT_MEM_LIMIT", "COLBERT_CPUS"),
    "neo4j": ("NEO4J_MEM_LIMIT", "NEO4J_CPUS"),
}


def render_env_fragment(
    allocation: Allocation, services: ServiceSet, knobs: dict[str, str]
) -> EnvFragment:
    """Render compose caps + feature vars + knob seed into an env fragment.

    Never targets the operator's ``.env``; the caller decides stdout vs ``.env.tuned``.
    """
    values: dict[str, str] = {}

    # Feature vars so `compose_files.py` reproduces the topology without re-flagging.
    values["ACCELERATOR"] = services.accelerator
    if services.tei and not services.tei_external:
        values["COMPOSE_PROFILES"] = "bundled-tei"
    values["RERANK_ENABLED"] = "true" if services.colbert else "false"
    if services.colbert:
        values["COLBERT_EMBED_BACKEND"] = "remote"
    values["GRAPH_ENABLED"] = "true" if services.neo4j else "false"

    # Compose caps for active services.
    for svc in services.active:
        mem_var, cpu_var = _CAP_ENV_NAMES[svc]
        if svc in allocation.ram_gib:
            values[mem_var] = _fmt_gib(allocation.ram_gib[svc])
        if svc in allocation.cpus:
            values[cpu_var] = str(allocation.cpus[svc])

    values.update(knobs)
    return EnvFragment(values=values)


def build_allocation(budget: Budget, services: ServiceSet) -> Allocation:
    """Convenience: compute RAM + CPU allocation together."""
    return Allocation(
        ram_gib=allocate_ram(budget, services),
        cpus=allocate_cpus(budget, services),
    )
