"""Pytest path bootstrap for benchmarks + repo-root scripts."""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BENCH = Path(__file__).resolve().parents[1]
for p in (str(ROOT), str(BENCH)):
    if p not in sys.path:
        sys.path.insert(0, p)
