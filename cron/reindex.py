#!/usr/bin/env python3
"""Daily cron: pull default branch for indexed repos and incremental re-index."""

from __future__ import annotations

import json
import logging
import os
import subprocess
import sys
import urllib.error
import urllib.request
from typing import Any

LOG = logging.getLogger("reindex")

MCP_BASE = os.environ.get("MCP_URL", "http://mcp_server:8000").rstrip("/")
MCP_URL = f"{MCP_BASE}/mcp"
AUTH_TOKEN = os.environ.get("MCP_AUTH_TOKEN", "")
WORKSPACE_ROOT = os.environ.get("WORKSPACE_ROOT", "/workspace")
INDEX_TIMEOUT = int(os.environ.get("INDEX_TIMEOUT", "1800"))
MCP_HTTP_TIMEOUT = int(os.environ.get("MCP_HTTP_TIMEOUT", "300"))
GIT_TIMEOUT = int(os.environ.get("GIT_TIMEOUT", "120"))


class McpClient:
    """Minimal MCP streamable-http client (JSON-RPC over POST + SSE)."""

    def __init__(self, url: str, auth_token: str = "") -> None:
        self._url = url
        self._auth_token = auth_token
        self._session_id: str | None = None
        self._request_id = 0

    def _next_id(self) -> int:
        self._request_id += 1
        return self._request_id

    def _headers(self) -> dict[str, str]:
        headers = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        }
        if self._auth_token:
            headers["Authorization"] = f"Bearer {self._auth_token}"
        if self._session_id:
            headers["Mcp-Session-Id"] = self._session_id
        return headers

    def _post(self, payload: dict[str, Any], timeout: int = MCP_HTTP_TIMEOUT) -> dict[str, Any]:
        body = json.dumps(payload).encode("utf-8")
        req = urllib.request.Request(
            self._url,
            data=body,
            headers=self._headers(),
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            sid = resp.headers.get("Mcp-Session-Id")
            if sid:
                self._session_id = sid
            content_type = resp.headers.get("Content-Type", "")
            if "text/event-stream" in content_type:
                return self._parse_sse(resp)
            raw = resp.read().decode("utf-8").strip()
            if not raw:
                return {}
            return json.loads(raw)

    @staticmethod
    def _parse_sse(resp: Any) -> dict[str, Any]:
        last: dict[str, Any] = {}
        for raw_line in resp:
            line = raw_line.decode("utf-8").rstrip("\n\r")
            if not line.startswith("data: "):
                continue
            data = line[6:].strip()
            if not data:
                continue
            msg = json.loads(data)
            if "id" in msg:
                last = msg
        return last

    def initialize(self) -> None:
        init_resp = self._post(
            {
                "jsonrpc": "2.0",
                "id": self._next_id(),
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {"name": "codeindexer-cron", "version": "1.0.0"},
                },
            }
        )
        if "error" in init_resp:
            raise RuntimeError(f"MCP initialize failed: {init_resp['error']}")

        # Notification (no id) — required by MCP after initialize.
        notif_req = urllib.request.Request(
            self._url,
            data=json.dumps({"jsonrpc": "2.0", "method": "notifications/initialized"}).encode(
                "utf-8"
            ),
            headers=self._headers(),
            method="POST",
        )
        try:
            with urllib.request.urlopen(notif_req, timeout=30):
                pass
        except urllib.error.HTTPError:
            pass

    def call_tool(self, name: str, arguments: dict[str, Any], timeout: int = MCP_HTTP_TIMEOUT) -> Any:
        resp = self._post(
            {
                "jsonrpc": "2.0",
                "id": self._next_id(),
                "method": "tools/call",
                "params": {"name": name, "arguments": arguments},
            },
            timeout=timeout,
        )
        if "error" in resp:
            raise RuntimeError(f"tools/call {name} failed: {resp['error']}")

        result = resp.get("result", {})
        if result.get("isError"):
            content = result.get("content", [])
            text = content[0].get("text", "") if content else ""
            raise RuntimeError(f"tool {name} error: {text}")

        structured = result.get("structuredContent")
        if structured is not None:
            return structured

        content = result.get("content", [])
        if not content:
            return None
        text = content[0].get("text", "")
        if not text:
            return None
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            return text


def run_git(
    cwd: str,
    *args: str,
    check: bool = True,
    timeout: int = GIT_TIMEOUT,
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=cwd,
        capture_output=True,
        text=True,
        check=check,
        timeout=timeout,
    )


def is_git_repo(path: str) -> bool:
    if os.path.exists(os.path.join(path, ".git")):
        return True
    return run_git(path, "rev-parse", "--git-dir", check=False).returncode == 0


def default_branch(repo_path: str) -> str | None:
    repo_name = os.path.basename(repo_path.rstrip("/\\"))
    out: subprocess.CompletedProcess[str] | None = None
    try:
        out = run_git(repo_path, "remote", "show", "origin", check=False)
    except FileNotFoundError:
        return None
    except subprocess.TimeoutExpired:
        LOG.warning(
            "[%s] remote show origin timed out after %ss",
            repo_name,
            GIT_TIMEOUT,
        )

    if out is not None and out.returncode == 0:
        for line in out.stdout.splitlines():
            if "HEAD branch:" in line:
                branch = line.split("HEAD branch:", 1)[1].strip()
                if branch and branch != "(unknown)":
                    return branch
    elif out is not None:
        err = out.stderr.strip() or out.stdout.strip()
        LOG.debug("[%s] remote show origin failed: %s", repo_name, err)

    for candidate in ("main", "master"):
        if run_git(repo_path, "rev-parse", "--verify", candidate, check=False).returncode == 0:
            return candidate
        ref = f"origin/{candidate}"
        if run_git(repo_path, "rev-parse", "--verify", ref, check=False).returncode == 0:
            return candidate
    return None


def sync_repo(repo_path: str, branch: str) -> tuple[bool, str]:
    """Fetch and fast-forward default branch only when safe. Returns (changed, message)."""
    current = run_git(repo_path, "rev-parse", "--abbrev-ref", "HEAD", check=False)
    if current.returncode != 0:
        return False, "cannot determine current branch"
    current_branch = current.stdout.strip()
    if current_branch != branch:
        return False, f"on branch {current_branch}, expected {branch} — skipping"

    status = run_git(repo_path, "status", "--porcelain", check=False)
    if status.returncode == 0 and status.stdout.strip():
        return False, "dirty working tree — skipping"

    try:
        fetch = run_git(repo_path, "fetch", "origin", branch, check=False)
        if fetch.returncode != 0:
            fetch = run_git(repo_path, "fetch", "origin", check=False)
            if fetch.returncode != 0:
                return False, f"git fetch failed: {fetch.stderr.strip()}"
    except subprocess.TimeoutExpired:
        return False, "git fetch timed out"

    local = run_git(repo_path, "rev-parse", branch, check=False)
    remote = run_git(repo_path, "rev-parse", f"origin/{branch}", check=False)
    if remote.returncode != 0:
        return False, f"no origin/{branch} after fetch"

    local_head = local.stdout.strip() if local.returncode == 0 else ""
    remote_head = remote.stdout.strip()
    if local.returncode != 0:
        return False, f"local branch {branch} missing — skipping"

    if local_head == remote_head:
        return False, "no changes"

    old_short = local_head[:8]
    try:
        pull = run_git(repo_path, "pull", "--ff-only", "origin", branch, check=False)
    except subprocess.TimeoutExpired:
        return False, "git pull timed out"
    if pull.returncode != 0:
        err = pull.stderr.strip() or pull.stdout.strip()
        return False, f"cannot fast-forward — skipping ({err})"

    new_short = run_git(repo_path, "rev-parse", branch).stdout.strip()[:8]
    return True, f"updated {old_short} -> {new_short}"


def collection_names(client: McpClient) -> list[str]:
    raw = client.call_tool("list_collections", {})
    if isinstance(raw, list):
        return [item["name"] for item in raw if isinstance(item, dict) and "name" in item]
    if isinstance(raw, dict) and "result" in raw:
        items = raw["result"]
        if isinstance(items, list):
            return [item["name"] for item in items if isinstance(item, dict) and "name" in item]
    raise RuntimeError(f"unexpected list_collections response: {raw!r}")


def main() -> int:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(message)s",
        stream=sys.stdout,
    )
    LOG.info("starting reindex job workspace=%s mcp=%s", WORKSPACE_ROOT, MCP_URL)

    client = McpClient(MCP_URL, AUTH_TOKEN)
    try:
        client.initialize()
    except urllib.error.URLError as e:
        LOG.error("cannot reach MCP server at %s: %s", MCP_URL, e)
        return 1
    except Exception as e:
        LOG.error("MCP initialize failed: %s", e)
        return 1

    try:
        names = collection_names(client)
    except Exception as e:
        LOG.error("list_collections failed: %s", e)
        return 1

    if not names:
        LOG.info("no indexed collections")
        return 0

    LOG.info("found %d collection(s): %s", len(names), ", ".join(names))
    reindexed = 0
    skipped = 0
    errors = 0

    for name in names:
        repo_path = os.path.join(WORKSPACE_ROOT, name)
        LOG.info("[%s] processing", name)

        if not os.path.isdir(repo_path):
            LOG.warning("[%s] workspace path missing: %s", name, repo_path)
            skipped += 1
            continue

        if not is_git_repo(repo_path):
            LOG.warning("[%s] not a git repository, skipping", name)
            skipped += 1
            continue

        branch = default_branch(repo_path)
        if not branch:
            LOG.warning("[%s] could not determine default branch, skipping", name)
            skipped += 1
            continue

        changed, msg = sync_repo(repo_path, branch)
        if not changed:
            LOG.info("[%s] %s on %s", name, msg, branch)
            skipped += 1
            continue

        LOG.info("[%s] %s on %s — starting incremental index", name, msg, branch)
        try:
            result = client.call_tool(
                "index_codebase",
                {
                    "path": name,
                    "force": False,
                    "wait": True,
                    "timeout": INDEX_TIMEOUT,
                },
                timeout=INDEX_TIMEOUT + 60,
            )
            status = result.get("status", result) if isinstance(result, dict) else result
            LOG.info("[%s] index complete: %s", name, status)
            reindexed += 1
        except Exception as e:
            LOG.error("[%s] index_codebase failed: %s", name, e)
            errors += 1

    LOG.info(
        "done: reindexed=%d skipped=%d errors=%d total=%d",
        reindexed,
        skipped,
        errors,
        len(names),
    )
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
