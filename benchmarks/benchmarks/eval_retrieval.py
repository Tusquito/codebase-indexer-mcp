"""Golden-set retrieval evaluation harness (ADR 0007 / ADR 0030 Phase 7).

Measures retrieval relevance (recall@k, MRR, NDCG@k) against a curated golden
set by calling Aspire/.NET ``search_codebase`` over MCP HTTP (``--mcp-url``).

Requires optional ``benchmark`` extra: ``uv sync --extra benchmark``.

Usage:
    python -m benchmarks.eval_retrieval --mcp-url http://127.0.0.1:8000/mcp --output eval-results.json
    python -m benchmarks.eval_retrieval --validate-labels
    python -m benchmarks.eval_retrieval --mcp-url http://127.0.0.1:8000/mcp --compare fixtures/eval_baseline.json --threshold 5
"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmarks._connectivity import qdrant_reachable  # noqa: E402
from benchmarks._settings import load_settings  # noqa: E402
from benchmarks.chunk_id import make_chunk_id  # noqa: E402
from benchmarks.label_anchor import (  # noqa: E402
    Anchor,
    PointIndex,
    load_point_index,
    parse_anchors,
    resolve_anchors,
)

DEFAULT_GOLDEN = Path(__file__).resolve().parent / "fixtures" / "golden_queries.jsonl"
DEFAULT_METRICS = ["recall@10", "mrr", "ndcg@10"]


def _settings(**overrides: object):
    """Backward-compatible alias for load_settings."""
    return load_settings(**overrides)


@dataclass
class GoldenEntry:
    query_id: str
    query_text: str
    collection: str
    labels: dict[str, int]
    tags: list[str] = field(default_factory=list)
    aliases: dict[str, int] = field(default_factory=dict)
    anchors: list[Anchor] = field(default_factory=list)
    ground_truth: str | None = None
    hop2_query_text: str | None = None


def load_golden(path: Path) -> list[GoldenEntry]:
    """Load golden queries from JSONL."""
    entries: list[GoldenEntry] = []
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        data = json.loads(line)
        labels = {str(k): int(v) for k, v in data.get("labels", {}).items()}
        aliases = {str(k): int(v) for k, v in data.get("aliases", {}).items()}
        entries.append(
            GoldenEntry(
                query_id=data["query_id"],
                query_text=data["query_text"],
                collection=data["collection"],
                labels=labels,
                tags=list(data.get("tags", [])),
                aliases=aliases,
                anchors=parse_anchors(data.get("anchors")),
                ground_truth=data.get("ground_truth"),
                hop2_query_text=data.get("hop2_query_text"),
            )
        )
    if not entries:
        raise ValueError(f"No golden queries found in {path}")
    return entries


def resolve_labels(entry: GoldenEntry) -> dict[str, int]:
    """Merge explicit chunk_id labels with rel_path:start_line aliases."""
    resolved = dict(entry.labels)
    prefix = f"{entry.collection}/"
    for alias, grade in entry.aliases.items():
        if ":" not in alias:
            raise ValueError(
                f"Alias {alias!r} in {entry.query_id} must be rel_path:start_line"
            )
        rel_path, start_s = alias.rsplit(":", 1)
        if not rel_path.startswith(prefix):
            rel_path = prefix + rel_path.lstrip("/")
        chunk_id = make_chunk_id(rel_path, int(start_s))
        resolved.setdefault(chunk_id, grade)
    return resolved


def resolve_entry_labels(
    entry: GoldenEntry,
    index: PointIndex,
) -> tuple[dict[str, int], dict[str, Any] | None]:
    """Resolve an entry's relevance labels against the live collection."""
    if not entry.anchors:
        return resolve_labels(entry), None
    labels, report = resolve_anchors(
        entry.anchors, index, collection=entry.collection
    )
    for cid, grade in entry.labels.items():
        labels[cid] = max(labels.get(cid, grade), grade)
    return labels, report.as_dict()


def score_for_ranx(rank: int, top_k: int) -> float:
    """Rank-based score for ranx Run (higher rank = higher score)."""
    return float(top_k - rank)


def build_run_dict_from_chunk_ids(chunk_ids: list[str], *, top_k: int) -> dict[str, float]:
    """Build ranx run dict from ordered chunk ids (MCP search_codebase path)."""
    return {
        cid: score_for_ranx(rank, top_k)
        for rank, cid in enumerate(chunk_ids[:top_k])
    }


class _McpHttpClient:
    """Minimal streamable-HTTP MCP client."""

    def __init__(self, url: str, timeout: int = 120) -> None:
        self._url = url.rstrip("/")
        self._timeout = timeout
        self._session_id: str | None = None
        self._rid = 0

    def _headers(self) -> dict[str, str]:
        h = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        }
        if self._session_id:
            h["Mcp-Session-Id"] = self._session_id
        return h

    def post(self, payload: dict) -> dict:
        import urllib.error
        import urllib.request

        self._rid += 1
        payload["id"] = self._rid
        req = urllib.request.Request(
            self._url,
            data=json.dumps(payload).encode(),
            headers=self._headers(),
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=self._timeout) as resp:
            if resp.headers.get("Mcp-Session-Id"):
                self._session_id = resp.headers.get("Mcp-Session-Id")
            if "text/event-stream" in (resp.headers.get("Content-Type") or ""):
                last: dict = {}
                for raw in resp:
                    line = raw.decode().rstrip()
                    if line.startswith("data: "):
                        msg = json.loads(line[6:])
                        if "id" in msg:
                            last = msg
                return last
            raw = resp.read().decode().strip()
            return json.loads(raw) if raw else {}

    def initialize(self) -> None:
        import urllib.error
        import urllib.request

        r = self.post(
            {
                "jsonrpc": "2.0",
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {"name": "eval-retrieval-mcp", "version": "1"},
                },
            }
        )
        if "error" in r:
            raise RuntimeError(r["error"])
        notif = urllib.request.Request(
            self._url,
            data=json.dumps(
                {"jsonrpc": "2.0", "method": "notifications/initialized"}
            ).encode(),
            headers=self._headers(),
            method="POST",
        )
        try:
            urllib.request.urlopen(notif, timeout=30)
        except urllib.error.HTTPError:
            pass

    def call_tool(self, name: str, arguments: dict) -> dict:
        r = self.post(
            {
                "jsonrpc": "2.0",
                "method": "tools/call",
                "params": {"name": name, "arguments": arguments},
            }
        )
        if "error" in r:
            raise RuntimeError(r["error"])
        result = r.get("result", {})
        if result.get("isError"):
            text = result.get("content", [{}])[0].get("text", "")
            raise RuntimeError(text)
        structured = result.get("structuredContent")
        if structured is not None:
            return structured
        text = result.get("content", [{}])[0].get("text", "{}")
        return json.loads(text)


async def run_evaluation_via_mcp(
    *,
    mcp_url: str,
    qdrant_url: str,
    golden_path: Path,
    top_k: int,
    collection_override: str | None,
    rerank_enabled: bool = False,
) -> dict[str, Any]:
    """Evaluate retrieval by calling Aspire/Host search_codebase over MCP HTTP."""
    from qdrant_client import AsyncQdrantClient

    entries = load_golden(golden_path)
    client = _McpHttpClient(mcp_url)
    client.initialize()

    qdrant_client = AsyncQdrantClient(url=qdrant_url)
    index_cache: dict[str, PointIndex] = {}

    async def _index_for(collection: str) -> PointIndex:
        if collection not in index_cache:
            index_cache[collection] = await load_point_index(qdrant_client, collection)
        return index_cache[collection]

    run: dict[str, dict[str, float]] = {}
    qrels: dict[str, dict[str, int]] = {}
    per_query: list[dict[str, Any]] = []
    drift_total = {"drifted": 0, "unresolved": 0}

    try:
        for entry in entries:
            collection = collection_override or entry.collection
            if entry.anchors:
                index = await _index_for(collection)
                labels, drift = resolve_entry_labels(entry, index)
                if drift is not None:
                    drift_total["drifted"] += drift["drifted"]
                    drift_total["unresolved"] += drift["unresolved"]
            else:
                labels = resolve_labels(entry)
            qrels[entry.query_id] = labels

            tool_args: dict[str, Any] = {
                "query": entry.query_text,
                "collection": collection,
                "top_k": top_k,
            }
            if rerank_enabled:
                tool_args["rerank"] = True
            payload = client.call_tool("search_codebase", tool_args)
            results = payload.get("results", []) if isinstance(payload, dict) else []
            chunk_ids = [
                str(r.get("chunk_id", "")) for r in results if isinstance(r, dict)
            ]
            run[entry.query_id] = build_run_dict_from_chunk_ids(chunk_ids, top_k=top_k)

            hit_ids = set(chunk_ids[:top_k])
            relevant = set(labels.keys())
            top_hit = chunk_ids[0] if chunk_ids else None
            per_query.append(
                {
                    "query_id": entry.query_id,
                    "collection": collection,
                    "tags": entry.tags,
                    "retrieved": len(chunk_ids),
                    "hits_in_top_k": len(hit_ids & relevant),
                    "labels": len(relevant),
                    "top_hit_in_labels": top_hit in relevant if top_hit else False,
                }
            )
    finally:
        await qdrant_client.close()

    try:
        from ranx import evaluate
    except ImportError as exc:
        raise SystemExit(
            "ranx is required for eval_retrieval. Install with: uv sync --extra benchmark"
        ) from exc

    metrics_raw = evaluate(qrels, run, DEFAULT_METRICS)
    if isinstance(metrics_raw, dict):
        metrics = {name: round(float(metrics_raw[name]), 6) for name in DEFAULT_METRICS}
    else:
        metrics = {DEFAULT_METRICS[0]: round(float(metrics_raw), 6)}

    metrics_by_tag = compute_tag_metrics(entries, qrels, run)
    return {
        "schema": 1,
        "params": {
            "golden": str(golden_path),
            "hybrid_search": True,
            "rerank_enabled": rerank_enabled,
            "top_k": top_k,
            "mcp_url": mcp_url,
            "qdrant_url": qdrant_url,
            "search_path": "dotnet_mcp_search_codebase",
            "note": "ADR 0030 Phase 7 Aspire/.NET cutover",
        },
        "metrics": metrics,
        "metrics_by_tag": metrics_by_tag,
        "per_query": per_query,
        "n_queries": len(entries),
        "label_drift": drift_total,
    }


def compute_tag_metrics(
    entries: list[GoldenEntry],
    qrels: dict[str, dict[str, int]],
    run: dict[str, dict[str, float]],
) -> dict[str, dict[str, float]]:
    """Per-tag ranx metrics (a query contributes to each of its tags)."""
    from ranx import evaluate

    by_tag: dict[str, list[str]] = {}
    for entry in entries:
        for tag in entry.tags or ["untagged"]:
            by_tag.setdefault(tag, []).append(entry.query_id)

    out: dict[str, dict[str, float]] = {}
    for tag, query_ids in sorted(by_tag.items()):
        sub_qrels = {qid: qrels[qid] for qid in query_ids}
        sub_run = {qid: run[qid] for qid in query_ids}
        raw = evaluate(sub_qrels, sub_run, DEFAULT_METRICS)
        if isinstance(raw, dict):
            out[tag] = {name: round(float(raw[name]), 6) for name in DEFAULT_METRICS}
        else:
            out[tag] = {DEFAULT_METRICS[0]: round(float(raw), 6)}
    return out


async def validate_labels(
    qdrant_url: str,
    entries: list[GoldenEntry],
) -> dict[str, Any]:
    """Re-resolve golden labels against Qdrant, reporting drift (ADR 0026)."""
    from qdrant_client import AsyncQdrantClient

    by_collection: dict[str, list[GoldenEntry]] = {}
    for entry in entries:
        by_collection.setdefault(entry.collection, []).append(entry)

    report: dict[str, Any] = {
        "collections": {},
        "unresolved_total": 0,
        "drifted_total": 0,
        "missing_total": 0,
    }
    client = AsyncQdrantClient(url=qdrant_url)
    try:
        for collection, coll_entries in sorted(by_collection.items()):
            index = await load_point_index(client, collection)
            drifted = 0
            unresolved = 0
            labeled = 0
            unresolved_keys: list[str] = []
            missing_legacy: list[str] = []

            for entry in coll_entries:
                if entry.anchors:
                    _labels, rep = resolve_entry_labels(entry, index)
                    assert rep is not None
                    labeled += rep["total"]
                    drifted += rep["drifted"]
                    unresolved += rep["unresolved"]
                    unresolved_keys.extend(rep["unresolved_keys"])
                else:
                    legacy = resolve_labels(entry)
                    labeled += len(legacy)
                    for cid in legacy:
                        if not index.has_chunk_id(cid):
                            missing_legacy.append(cid)

            report["collections"][collection] = {
                "labeled": labeled,
                "drifted": drifted,
                "unresolved": unresolved,
                "unresolved_keys": sorted(unresolved_keys),
                "missing_legacy": sorted(missing_legacy),
            }
            report["drifted_total"] += drifted
            report["unresolved_total"] += unresolved
            report["missing_total"] += len(missing_legacy)
    finally:
        await client.close()

    return report


def render_table(result: dict[str, Any]) -> str:
    params = result["params"]
    lines = [
        "=" * 64,
        f"Retrieval eval  queries={result['n_queries']}  "
        f"hybrid={params['hybrid_search']}  rerank={params.get('rerank_enabled', False)}  "
        f"top_k={params['top_k']}",
        "-" * 64,
    ]
    for name, value in result["metrics"].items():
        lines.append(f"  {name:<20}{value:>12.4f}")
    by_tag = result.get("metrics_by_tag") or {}
    if by_tag:
        lines.append("-" * 64)
        lines.append("  By tag:")
        for tag, tag_metrics in sorted(by_tag.items()):
            recall = tag_metrics.get("recall@10", 0.0)
            mrr = tag_metrics.get("mrr", 0.0)
            lines.append(f"    {tag:<16} recall@10={recall:.4f}  mrr={mrr:.4f}")
    lines.append("=" * 64)
    return "\n".join(lines)


def compare(
    current: dict[str, Any],
    baseline: dict[str, Any],
    threshold_pct: float,
) -> tuple[str, bool]:
    """Return (report, regressed)."""
    lines = ["", "Comparison vs baseline (negative = worse):", "-" * 72]
    regressed = False

    for name in DEFAULT_METRICS:
        cur = current["metrics"].get(name, 0.0)
        base = baseline["metrics"].get(name, 0.0)
        if base == 0:
            pct = 0.0 if cur == 0 else 100.0
        else:
            pct = (cur - base) / base * 100.0
        worse = pct < -threshold_pct
        flag = "  REGRESSION" if worse else ""
        if worse:
            regressed = True
        lines.append(
            f"  {name:<34}{base:>10.4f} -> {cur:>10.4f}  ({pct:+6.1f}%){flag}"
        )

    lines.append("-" * 72)
    return "\n".join(lines), regressed


def main() -> int:
    parser = argparse.ArgumentParser(description="Golden-set retrieval evaluation (ranx)")
    parser.add_argument(
        "--golden",
        type=Path,
        default=Path(os.environ.get("EVAL_GOLDEN", DEFAULT_GOLDEN)),
    )
    parser.add_argument(
        "--qdrant-url",
        default=os.environ.get("QDRANT_URL", "http://localhost:6333"),
    )
    parser.add_argument("--collection", help="Override collection for all queries.")
    parser.add_argument("--top-k", type=int, default=int(os.environ.get("EVAL_TOP_K", "10")))
    parser.add_argument(
        "--rerank",
        action="store_true",
        help="Enable ColBERT reranking (requires indexed colbert multivectors).",
    )
    parser.add_argument(
        "--validate-labels",
        action="store_true",
        help="Check labeled chunk_ids exist in Qdrant; skip search.",
    )
    parser.add_argument("--output", help="Write results JSON to this path.")
    parser.add_argument(
        "--mcp-url",
        default=os.environ.get("EVAL_MCP_URL"),
        help="Aspire/Host MCP HTTP URL (required for search eval).",
    )
    parser.add_argument("--compare", help="Baseline JSON to compare against.")
    parser.add_argument(
        "--threshold",
        type=float,
        default=float(os.environ.get("EVAL_THRESHOLD", "0")),
        help="Fail (exit 1) if a metric drops more than this percent vs baseline.",
    )
    # Compat no-ops removed from Python search path.
    parser.add_argument("--tei-url", default=None, help=argparse.SUPPRESS)
    parser.add_argument("--no-hybrid", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()

    if not qdrant_reachable(args.qdrant_url):
        print(f"SKIP: Qdrant not reachable at {args.qdrant_url}", file=sys.stderr)
        return 0

    if args.validate_labels:
        entries = load_golden(args.golden)
        report = asyncio.run(validate_labels(args.qdrant_url, entries))
        print(json.dumps(report, indent=2))
        if report["drifted_total"]:
            print(
                f"INFO: {report['drifted_total']} anchor(s) re-resolved after line drift",
                file=sys.stderr,
            )
        unresolved = report["unresolved_total"] + report["missing_total"]
        if unresolved:
            print(
                f"WARN: {unresolved} label(s) unresolved against Qdrant",
                file=sys.stderr,
            )
            return 1
        return 0

    if not args.mcp_url:
        print(
            "ERROR: --mcp-url is required for search evaluation "
            "(Python run_search path removed in ADR 0030 Phase 7).",
            file=sys.stderr,
        )
        return 2

    result = asyncio.run(
        run_evaluation_via_mcp(
            mcp_url=args.mcp_url,
            qdrant_url=args.qdrant_url,
            golden_path=args.golden,
            top_k=args.top_k,
            collection_override=args.collection,
            rerank_enabled=args.rerank,
        )
    )

    print(render_table(result))

    if args.output:
        Path(args.output).write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(f"\nWrote {args.output}")

    if args.compare:
        baseline = json.loads(Path(args.compare).read_text(encoding="utf-8"))
        report, regressed = compare(result, baseline, args.threshold or 1e9)
        print(report)
        if args.threshold and regressed:
            print(
                "FAIL: one or more metrics regressed beyond threshold",
                file=sys.stderr,
            )
            return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
