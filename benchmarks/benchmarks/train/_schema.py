"""Training pair schema for contrastive fine-tuning (ADR 0020)."""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any


@dataclass
class TrainingPair:
    """One query–passage contrastive row with optional hard negatives."""

    query_id: str
    query: str
    positive: str
    negatives: list[str] = field(default_factory=list)
    tags: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> TrainingPair:
        return cls(
            query_id=str(data["query_id"]),
            query=str(data["query"]),
            positive=str(data["positive"]),
            negatives=[str(n) for n in data.get("negatives", [])],
            tags=[str(t) for t in data.get("tags", [])],
        )


def write_jsonl(pairs: list[TrainingPair], path: Path) -> None:
    """Serialize training pairs to JSONL."""
    lines = [json.dumps(pair.to_dict(), ensure_ascii=False) for pair in pairs]
    path.write_text("\n".join(lines) + ("\n" if lines else ""), encoding="utf-8")


def read_jsonl(path: Path) -> list[TrainingPair]:
    """Load training pairs from JSONL."""
    pairs: list[TrainingPair] = []
    for line_no, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        try:
            data = json.loads(line)
        except json.JSONDecodeError as exc:
            raise ValueError(f"Invalid JSON on line {line_no} of {path}") from exc
        pairs.append(TrainingPair.from_dict(data))
    return pairs
