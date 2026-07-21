#!/usr/bin/env python3
"""Multi-collection map_service_dependencies smoke for --aspire-stack (.NET MCP).

Indexes two tiny fixture trees under the compose-mounted workspace
(WORKSPACE_PATH / ASPIRE_SMOKE_WORKSPACE / cwd → /workspace in the MCP
container) then calls map_service_dependencies. Requires live MCP at MCP_URL.
"""
from __future__ import annotations

import json
import os
import shutil
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

MCP_URL = os.environ.get("MCP_URL", "http://127.0.0.1:8000/mcp")
TIMEOUT = 180
FIXTURE_NAMES = ("smoke-svc-a", "smoke-svc-b")


def _resolve_workspace() -> Path:
    """Host path mounted as /workspace (docker-compose.aspire.yml WORKSPACE_PATH)."""
    for key in ("ASPIRE_SMOKE_WORKSPACE", "WORKSPACE_PATH"):
        raw = os.environ.get(key)
        if raw:
            return Path(raw).expanduser().resolve()
    return Path.cwd().resolve()


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
                    "clientInfo": {"name": "smoke-aspire-service-map", "version": "1"},
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


def _write_fixtures(root: Path) -> tuple[str, str]:
    a = root / "smoke-svc-a"
    b = root / "smoke-svc-b"
    a.mkdir(parents=True, exist_ok=True)
    b.mkdir(parents=True, exist_ok=True)
    # Substantive enough for TreeSitter to emit chunks (tiny one-liners yield 0).
    (a / "Client.cs").write_text(
        "\n".join(
            [
                "using System.Net.Http;",
                "",
                "namespace SmokeSvcA;",
                "",
                "public class UsersClient",
                "{",
                "    private readonly HttpClient _http = new();",
                "",
                "    public void CallUsersList()",
                "    {",
                '        var path = "/api/users/list";',
                "        _ = _http.GetAsync(path);",
                "    }",
                "}",
                "",
            ]
        ),
        encoding="utf-8",
    )
    (b / "UsersController.cs").write_text(
        "\n".join(
            [
                "using Microsoft.AspNetCore.Mvc;",
                "",
                "namespace SmokeSvcB;",
                "",
                '[Route("api/users")]',
                "public class UsersController : ControllerBase",
                "{",
                '    [HttpGet("list")]',
                "    public IActionResult List()",
                "    {",
                '        return Ok("users");',
                "    }",
                "}",
                "",
            ]
        ),
        encoding="utf-8",
    )
    return a.name, b.name


def _wait_index(client: McpClient, job_id: str, timeout_s: float = 120.0) -> None:
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        status = client.call_tool("index_status", {"job_id": job_id})
        state = (status.get("status") or status.get("state") or "").lower()
        if state in ("completed", "complete", "done", "succeeded", "success"):
            return
        if state in ("failed", "error", "cancelled"):
            raise RuntimeError(f"index job failed: {status}")
        time.sleep(2)
    raise TimeoutError(f"index job {job_id} did not finish")


def _cleanup_fixtures(workspace: Path) -> None:
    for name in FIXTURE_NAMES:
        target = workspace / name
        if target.is_dir():
            shutil.rmtree(target, ignore_errors=True)


def main() -> int:
    workspace = _resolve_workspace()
    coll_a, coll_b = _write_fixtures(workspace)
    print(f"fixtures under {workspace}: {coll_a}, {coll_b}")

    try:
        client = McpClient()
        client.initialize()

        for coll in (coll_a, coll_b):
            started = client.call_tool("index_codebase", {"path": coll, "force": True})
            job_id = started.get("job_id") or started.get("id")
            if not job_id:
                # Some hosts return immediately without job id when sync — accept empty error-free start
                print(f"index_codebase({coll}) => {started}")
                continue
            total = started.get("total_files") or started.get("totalFiles")
            if total is not None:
                print(f"index_codebase({coll}) total_files={total}")
            _wait_index(client, str(job_id))

        result = client.call_tool(
            "map_service_dependencies",
            {"collections": [coll_a, coll_b], "top_k": 10},
        )
        if "error" in result:
            print("FAIL:", result)
            return 1
        print(
            json.dumps(
                {
                    "collections_analyzed": result.get("collections_analyzed"),
                    "summary": result.get("summary"),
                },
                indent=2,
            )
        )
        analyzed = result.get("collections_analyzed") or []
        if coll_a not in analyzed or coll_b not in analyzed:
            print("FAIL: expected both collections in collections_analyzed")
            return 1
        print("PASS: map_service_dependencies multi-collection smoke")
        return 0
    finally:
        _cleanup_fixtures(workspace)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:  # noqa: BLE001 — smoke script surfaces any failure
        print(f"FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
