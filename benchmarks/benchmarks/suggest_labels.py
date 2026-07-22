"""Suggest golden-set label candidates via Aspire/Host MCP HTTP search.

Usage:
    python -m benchmarks.suggest_labels "ChunkId FromPathAndLine" --mcp-url http://127.0.0.1:8000/mcp
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


class _McpHttpClient:
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
        r = self.post(
            {
                "jsonrpc": "2.0",
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {"name": "suggest-labels", "version": "1"},
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


def _strip_collection_prefix(rel_path: str, collection: str) -> str:
    prefix = f"{collection}/"
    if rel_path.startswith(prefix):
        return rel_path[len(prefix) :]
    return rel_path


def main() -> int:
    parser = argparse.ArgumentParser(description="Suggest golden labels via MCP search")
    parser.add_argument("query")
    parser.add_argument("--collection", default="codebase-indexer-mcp")
    parser.add_argument("--top-k", type=int, default=8)
    parser.add_argument(
        "--mcp-url",
        default=os.environ.get("EVAL_MCP_URL", "http://127.0.0.1:8000/mcp"),
    )
    args = parser.parse_args()

    client = _McpHttpClient(args.mcp_url)
    client.initialize()
    payload = client.call_tool(
        "search_codebase",
        {
            "query": args.query,
            "collection": args.collection,
            "top_k": args.top_k,
        },
    )
    results = payload.get("results", []) if isinstance(payload, dict) else []

    print(f"Query: {args.query!r}")
    print(f"Collection: {args.collection}  top_k={args.top_k}\n")
    print(f"{'rank':<5}{'alias':<70}{'symbol':<28}{'score'}")
    print("-" * 110)
    for rank, r in enumerate(results, start=1):
        if not isinstance(r, dict):
            continue
        rel = str(r.get("rel_path", ""))
        start = int(r.get("start_line") or 0)
        alias = f"{_strip_collection_prefix(rel, args.collection)}:{start}"
        sym = str(r.get("symbol_name") or "-")
        score = float(r.get("score") or 0.0)
        print(f"{rank:<5}{alias:<70}{sym:<28}{score:.4f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
