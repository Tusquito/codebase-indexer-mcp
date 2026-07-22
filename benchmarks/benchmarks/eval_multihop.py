"""Multi-hop eval — deferred MCP-HTTP port (ADR 0030 Phase 7).

``eval_retrieval --mcp-url`` is the production quality path. Multi-hop hop
fusion remains available in ``benchmarks.multihop_rrf`` for offline tests.
"""

from __future__ import annotations

import sys


def main() -> int:
    print(
        "SKIP: eval_multihop Python run_search path removed. "
        "Use benchmarks.eval_retrieval --mcp-url for quality validation.",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
