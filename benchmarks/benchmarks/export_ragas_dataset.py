"""Export golden set for client-side Ragas pipeline evaluation (ADR 0010).

Produces JSON rows integrators load into Ragas ``EvaluationDataset`` or a
custom eval loop. Retrieval metrics stay in ``eval_retrieval.py``; this export
supplies ``question``, optional ``ground_truth`` (reference answer for
``context_precision``), and metadata to join with ranx ``recall@10`` by
``query_id``.

Usage:
    python -m benchmarks.export_ragas_dataset
    python -m benchmarks.export_ragas_dataset --output ragas-golden.json
    python -m benchmarks.export_ragas_dataset --require-ground-truth
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks.eval_retrieval import load_golden  # noqa: E402

DEFAULT_GOLDEN = Path(__file__).resolve().parent / "fixtures" / "golden_queries.jsonl"


def export_rows(entries: list, *, require_ground_truth: bool = False) -> list[dict]:
    """Build Ragas-oriented sample dicts from golden entries."""
    rows: list[dict] = []
    for entry in entries:
        if require_ground_truth and not entry.ground_truth:
            continue
        row: dict = {
            "query_id": entry.query_id,
            "question": entry.query_text,
            "collection": entry.collection,
            "tags": entry.tags,
        }
        if entry.ground_truth:
            row["ground_truth"] = entry.ground_truth
        rows.append(row)
    return rows


def main() -> None:
    parser = argparse.ArgumentParser(description="Export golden set for client-side Ragas eval")
    parser.add_argument(
        "--golden",
        type=Path,
        default=DEFAULT_GOLDEN,
        help="Path to golden_queries.jsonl",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Write JSON array to file (default: stdout)",
    )
    parser.add_argument(
        "--require-ground-truth",
        action="store_true",
        help="Only export queries with a ground_truth field",
    )
    args = parser.parse_args()

    entries = load_golden(args.golden)
    rows = export_rows(entries, require_ground_truth=args.require_ground_truth)
    payload = json.dumps(rows, indent=2, ensure_ascii=False) + "\n"

    if args.output:
        args.output.write_text(payload, encoding="utf-8")
        print(f"Wrote {len(rows)} rows to {args.output}", file=sys.stderr)
    else:
        sys.stdout.write(payload)


if __name__ == "__main__":
    main()
