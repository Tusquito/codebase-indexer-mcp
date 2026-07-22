"""Verify bake-off candidate — deferred after Python runtime removal.

Use Aspire MCP + TEI smoke via ``scripts/run_compose_integration.py`` instead.
"""

from __future__ import annotations

import sys


def main() -> int:
    print(
        "SKIP: verify_candidate requires deleted Python embed registry "
        "(ADR 0030 Phase 7). Use aspire-stack TEI embed smoke instead.",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
