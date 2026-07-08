"""Repeat-run reproducibility regression test (ADR 0026 Phase 1).

Directly guards the ADR 0021 defect: running ``eval_retrieval.py`` twice
against an *unchanged* indexed collection must produce identical metrics.
The old line-anchored labels could silently score against stale chunk_ids on
the second run; content anchoring must make the harness deterministic.

Live test — self-skips when Qdrant/TEI or the indexed collection are absent.
"""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks._connectivity import qdrant_reachable, tei_reachable  # noqa: E402
from benchmarks.eval_retrieval import run_evaluation  # noqa: E402

GOLDEN = Path(__file__).resolve().parents[1] / "benchmarks" / "fixtures" / "golden_queries.jsonl"
QDRANT_URL = "http://localhost:6333"
TEI_URL = "http://localhost:8080"
COLLECTION = "codebase-indexer-mcp"

METRICS = ("recall@10", "mrr", "ndcg@10")
TOLERANCE = 1e-9


@pytest.mark.benchmark
@pytest.mark.asyncio
@pytest.mark.skipif(not qdrant_reachable(QDRANT_URL), reason="Qdrant not reachable")
@pytest.mark.skipif(not tei_reachable(TEI_URL), reason="TEI not reachable")
async def test_repeat_run_metrics_are_identical():
    from codebase_indexer.config import Settings
    from codebase_indexer.storage.qdrant import QdrantStorage

    storage = QdrantStorage(Settings(qdrant_url=QDRANT_URL))
    client = await storage._get_client()
    names = {c.name for c in (await client.get_collections()).collections}
    if COLLECTION not in names:
        pytest.skip(f"{COLLECTION} collection not indexed")

    async def _run() -> dict:
        return await run_evaluation(
            qdrant_url=QDRANT_URL,
            tei_url=TEI_URL,
            golden_path=GOLDEN,
            hybrid_search=True,
            rerank_enabled=False,
            top_k=10,
            collection_override=None,
        )

    first = await _run()
    second = await _run()

    for name in METRICS:
        assert abs(first["metrics"][name] - second["metrics"][name]) <= TOLERANCE, (
            f"metric {name} not reproducible: "
            f"{first['metrics'][name]} != {second['metrics'][name]}"
        )

    # No anchor should be silently unresolved on an indexed collection.
    assert first["label_drift"]["unresolved"] == 0
    assert second["label_drift"]["unresolved"] == 0
