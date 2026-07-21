#!/usr/bin/env python3
"""Trigger force re-index via MCP HTTP."""
import json
import os
import sys
import urllib.error
import urllib.request

MCP_URL = os.environ.get("MCP_URL", "http://127.0.0.1:8000/mcp")
TIMEOUT = 3600


class McpClient:
    def __init__(self, url: str = MCP_URL) -> None:
        self._url = url
        self._session_id = None
        self._rid = 0

    def _headers(self):
        h = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        }
        if self._session_id:
            h["Mcp-Session-Id"] = self._session_id
        return h

    def post(self, payload, timeout=TIMEOUT):
        self._rid += 1
        payload["id"] = self._rid
        req = urllib.request.Request(
            self._url,
            data=json.dumps(payload).encode(),
            headers=self._headers(),
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            if resp.headers.get("Mcp-Session-Id"):
                self._session_id = resp.headers.get("Mcp-Session-Id")
            if "text/event-stream" in resp.headers.get("Content-Type", ""):
                last = {}
                for raw in resp:
                    line = raw.decode().rstrip()
                    if line.startswith("data: "):
                        msg = json.loads(line[6:])
                        if "id" in msg:
                            last = msg
                return last
            raw = resp.read().decode().strip()
            return json.loads(raw) if raw else {}

    def initialize(self):
        r = self.post(
            {
                "jsonrpc": "2.0",
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {"name": "reindex-graph", "version": "1"},
                },
            }
        )
        if "error" in r:
            raise RuntimeError(r["error"])
        notif = urllib.request.Request(
            self._url,
            data=json.dumps({"jsonrpc": "2.0", "method": "notifications/initialized"}).encode(),
            headers=self._headers(),
            method="POST",
        )
        try:
            urllib.request.urlopen(notif, timeout=30)
        except urllib.error.HTTPError:
            pass

    def call_tool(self, name, arguments):
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


def main():
    client = McpClient()
    client.initialize()
    print("Calling index_codebase(path='codebase-indexer-mcp', force=True, wait=True)...")
    result = client.call_tool(
        "index_codebase",
        {
            "path": "codebase-indexer-mcp",
            "force": True,
            "wait": True,
            "timeout": 3600,
        },
    )
    print(json.dumps(result, indent=2))
    status = result.get("status") or result.get("state")
    if result.get("error"):
        return 1
    if status in ("failed", "error"):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
