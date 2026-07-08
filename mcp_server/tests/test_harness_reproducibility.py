"""Repeat-run reproducibility regression test (ADR 0026 Phase 1).

Directly guards the ADR 0021 defect: running ``eval_retrieval.py`` twice
against an *unchanged* indexed collection must resolve golden labels
identically and reproduce ``recall@10`` within the ADR's +/-1pp success
criterion. The old line-anchored labels could silently score against stale
chunk_ids on the second run; content anchoring must make label resolution
deterministic. Rank-sensitive metrics (mrr, ndcg@10) are checked for bounded
variance rather than byte-exact equality — see ``RANKING_TOLERANCE`` below.

Live test — self-skips when Qdrant/TEI or the indexed collection are absent.
"""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks._connectivity import qdrant_reachable, tei_reachable  # noqa: E402
from benchmarks.eval_retrieval import (  # noqa: E402
    load_golden,
    resolve_entry_labels,
    resolve_labels,
    run_evaluation,
)
from benchmarks.label_anchor import PointIndex, load_point_index  # noqa: E402

GOLDEN = Path(__file__).resolve().parents[1] / "benchmarks" / "fixtures" / "golden_queries.jsonl"
QDRANT_URL = "http://localhost:6333"
TEI_URL = "http://localhost:8080"
COLLECTION = "codebase-indexer-mcp"

# ADR 0026 success criterion #1: recall@10 must be stable within +/-1
# percentage point across repeat runs on an unchanged corpus. This is the
# gate this test exists to enforce (fixes the 0021 defect).
RECALL_METRIC = "recall@10"
RECALL_TOLERANCE = 0.01

# mrr / ndcg@10 are rank-sensitive metrics, not the ADR success criterion.
# Qdrant's server-side RRF fusion (Fusion.RRF in _hybrid_rrf_query) and
# approximate HNSW search can legitimately re-order near-tied candidates
# between otherwise-identical queries, nudging these metrics slightly
# without changing which chunks are retrieved (recall@10 stays stable) or
# how labels resolve. That reordering lives inside Qdrant's engine, not in
# our retrieval code, so there is no small/safe client-side tie-break to
# add here. A generous epsilon (observed swings are ~0.02) still catches
# real regressions without asserting byte-exact reproducibility.
RANKING_METRICS = ("mrr", "ndcg@10")
RANKING_TOLERANCE = 0.05


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

    async def _resolve_all_labels() -> dict[str, dict[str, int]]:
        """Resolve every golden entry's labels against the live collection.

        This is what the ADR 0026 label-resolution ladder must make
        deterministic: the same content anchors must resolve to the same
        chunk_id -> grade mapping every time, independent of any search
        ranking. This is the actual gate the repeat-run test exists to
        enforce (the ADR 0021 defect was stale label resolution, not
        ranking-metric jitter).
        """
        index_cache: dict[str, PointIndex] = {}
        resolved: dict[str, dict[str, int]] = {}
        for entry in load_golden(GOLDEN):
            if entry.collection not in index_cache:
                index_cache[entry.collection] = await load_point_index(
                    client, entry.collection
                )
            index = index_cache[entry.collection]
            if entry.anchors:
                labels, _ = resolve_entry_labels(entry, index)
            else:
                labels = resolve_labels(entry)
            resolved[entry.query_id] = labels
        return resolved

    labels_before = await _resolve_all_labels()
    first = await _run()
    second = await _run()
    labels_after = await _resolve_all_labels()

    # Label resolution must be exactly deterministic across the run window:
    # the same anchors must resolve to the same chunk_id -> grade mapping.
    assert labels_before == labels_after, (
        "label resolution is not deterministic across repeat runs"
    )

    # ADR 0026 success criterion #1: recall@10 stable within +/-1pp.
    recall_diff = abs(first["metrics"][RECALL_METRIC] - second["metrics"][RECALL_METRIC])
    assert recall_diff <= RECALL_TOLERANCE, (
        f"metric {RECALL_METRIC} not reproducible within +/-{RECALL_TOLERANCE}: "
        f"{first['metrics'][RECALL_METRIC]} != {second['metrics'][RECALL_METRIC]}"
    )

    # Rank-sensitive metrics: bounded variance allowed, not byte-exact (see
    # RANKING_TOLERANCE comment above for why).
    for name in RANKING_METRICS:
        diff = abs(first["metrics"][name] - second["metrics"][name])
        assert diff <= RANKING_TOLERANCE, (
            f"metric {name} varied more than expected ({RANKING_TOLERANCE}): "
            f"{first['metrics'][name]} != {second['metrics'][name]}"
        )

    # No anchor should be silently unresolved on an indexed collection.
    assert first["label_drift"]["unresolved"] == 0
    assert second["label_drift"]["unresolved"] == 0
