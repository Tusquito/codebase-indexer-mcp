#!/usr/bin/env python3
"""Smoke-test MCP tools against local stack (http://127.0.0.1:8000/mcp)."""
from __future__ import annotations

import json
import sys
import urllib.error
import urllib.request

MCP_URL = "http://127.0.0.1:8000/mcp"
TIMEOUT = 120


class McpClient:
    def __init__(self) -> None:
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

    def post(self, payload: dict, timeout: int = TIMEOUT) -> dict:
        self._rid += 1
        payload["id"] = self._rid
        req = urllib.request.Request(
            MCP_URL,
            data=json.dumps(payload).encode(),
            headers=self._headers(),
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            if resp.headers.get("Mcp-Session-Id"):
                self._session_id = resp.headers.get("Mcp-Session-Id")
            if "text/event-stream" in resp.headers.get("Content-Type", ""):
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
                    "clientInfo": {"name": "smoke-mcp-tools", "version": "1"},
                },
            }
        )
        if "error" in r:
            raise RuntimeError(r["error"])
        notif = urllib.request.Request(
            MCP_URL,
            data=json.dumps({"jsonrpc": "2.0", "method": "notifications/initialized"}).encode(),
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


def _call_sites(result: dict) -> list[dict]:
    sites = []
    found_in = result.get("found_in", {})
    if isinstance(found_in, dict):
        for items in found_in.values():
            for item in items:
                if item.get("match_type") == "call_site":
                    sites.append(item)
    elif isinstance(found_in, list):
        for item in found_in:
            if isinstance(item, dict) and item.get("match_type") == "call_site":
                sites.append(item)
    return sites


def main() -> int:
    client = McpClient()
    client.initialize()
    collection = "codebase-indexer-mcp"

    print("=== list_collections ===")
    cols = client.call_tool("list_collections", {})
    print(json.dumps(cols, indent=2))

    print("\n=== get_collection_summary ===")
    summary = client.call_tool("get_collection_summary", {"collection": collection})
    print(json.dumps({k: summary[k] for k in ("collection", "total_files", "total_chunks") if k in summary}, indent=2))

    print("\n=== find_cross_references (Path D — member=find_callers) ===")
    xref = client.call_tool(
        "find_cross_references",
        {
            "collections": [collection],
            "member": "find_callers",
            "symbol_name": "find_callers",
            "top_k": 10,
        },
    )
    sites = _call_sites(xref)
    print(f"call_site hits: {len(sites)}")
    for s in sites[:8]:
        print(f"  - {s.get('symbol_name')} @ {s.get('rel_path')}:{s.get('start_line')}")

    print("\n=== find_cross_references (Path D — receiver=graph_storage, member=find_callers) ===")
    xref2 = client.call_tool(
        "find_cross_references",
        {
            "collections": [collection],
            "member": "find_callers",
            "receiver": "graph_storage",
            "top_k": 10,
        },
    )
    sites2 = _call_sites(xref2)
    print(f"call_site hits: {len(sites2)}")
    for s in sites2[:8]:
        print(f"  - {s.get('symbol_name')} @ {s.get('rel_path')}:{s.get('start_line')}")

    print("\n=== search_symbols (Neo4jStorage.find_callers) ===")
    syms = client.call_tool(
        "search_symbols",
        {"query": "Neo4jStorage find_callers", "collection": collection, "top_k": 5},
    )
    for hit in syms.get("results", syms.get("symbols", []))[:5]:
        if isinstance(hit, dict):
            print(f"  - {hit.get('symbol_name')} @ {hit.get('rel_path')}")

    print("\n=== search_codebase (truncated) ===")
    search = client.call_tool(
        "search_codebase",
        {
            "query": "Neo4j call site lookup find_callers",
            "collection": collection,
            "top_k": 3,
            "max_content_chars": 200,
        },
    )
    for hit in search.get("results", [])[:3]:
        print(f"  - score={hit.get('score', 0):.3f} {hit.get('rel_path')} chunk={hit.get('chunk_id', '')[:12]}...")

    ok = len(sites2) > 0 and any(
        "cross_references.py" in s.get("rel_path", "") for s in sites2
    )
    print(f"\n{'PASS' if ok else 'FAIL'}: expected call_site in cross_references.py for graph_storage.find_callers")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
