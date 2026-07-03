"""Export golden-set query–positive pairs for contrastive training (ADR 0020).

Resolves labeled chunk content from Qdrant and writes JSONL rows matching
the training schema in ``benchmarks/train/_schema.py``.

Usage:
    python -m benchmarks.train.export_golden_pairs
    python -m benchmarks.train.export_golden_pairs --output pairs.jsonl
    python -m benchmarks.train.export_golden_pairs --validate-labels
"""

from __future__ import annotations

import argparse
import asyncio
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src"))

from benchmarks.eval_retrieval import DEFAULT_GOLDEN, GoldenEntry, load_golden  # noqa: E402
from benchmarks.train._positives import resolve_positive_passage  # noqa: E402
from benchmarks.train._schema import TrainingPair, write_jsonl  # noqa: E402
from codebase_indexer.storage.qdrant import QdrantStorage  # noqa: E402

from benchmarks._settings import load_settings  # noqa: E402


async def export_pairs(
    entries: list[GoldenEntry],
    storage: QdrantStorage,
    *,
    collection_override: str | None = None,
) -> tuple[list[TrainingPair], list[str]]:
    """Build training pairs; return (pairs, error_messages)."""
    pairs: list[TrainingPair] = []
    errors: list[str] = []
    for entry in entries:
        collection = collection_override or entry.collection
        try:
            positive = await resolve_positive_passage(
                storage, entry, collection=collection
            )
        except (LookupError, ValueError) as exc:
            errors.append(f"{entry.query_id}: {exc}")
            continue
        pairs.append(
            TrainingPair(
                query_id=entry.query_id,
                query=entry.query_text,
                positive=positive,
                tags=list(entry.tags),
            )
        )
    return pairs, errors


async def run_export(
    golden_path: Path,
    output_path: Path,
    *,
    qdrant_url: str,
    collection_override: str | None,
    fail_on_missing: bool,
) -> int:
    settings = load_settings(qdrant_url=qdrant_url)
    entries = load_golden(golden_path)
    storage = QdrantStorage(settings)

    pairs, errors = await export_pairs(
        entries, storage, collection_override=collection_override
    )

    if errors:
        for msg in errors:
            print(f"WARN: {msg}", file=sys.stderr)
        if fail_on_missing:
            print(
                f"ERROR: {len(errors)} entries failed label resolution",
                file=sys.stderr,
            )
            return 1

    write_jsonl(pairs, output_path)
    print(
        f"Wrote {len(pairs)} pairs to {output_path} "
        f"({len(errors)} skipped)",
        file=sys.stderr,
    )
    return 0 if pairs else 1


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Export golden-set query–positive pairs for fine-tuning"
    )
    parser.add_argument(
        "--golden",
        type=Path,
        default=DEFAULT_GOLDEN,
        help="Path to golden_queries.jsonl",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).resolve().parent / "outputs" / "golden_pairs.jsonl",
        help="Output JSONL path",
    )
    parser.add_argument(
        "--qdrant-url",
        default="http://localhost:6333",
        help="Qdrant HTTP URL",
    )
    parser.add_argument(
        "--collection",
        default=None,
        help="Override collection name for all entries",
    )
    parser.add_argument(
        "--validate-labels",
        action="store_true",
        help="Exit non-zero when any golden label cannot be resolved",
    )
    args = parser.parse_args()

    args.output.parent.mkdir(parents=True, exist_ok=True)
    raise SystemExit(
        asyncio.run(
            run_export(
                args.golden,
                args.output,
                qdrant_url=args.qdrant_url,
                collection_override=args.collection,
                fail_on_missing=args.validate_labels,
            )
        )
    )


if __name__ == "__main__":
    main()
