# src/codebase_indexer/stdio_proxy.py
"""Lightweight stdio-to-HTTP proxy for MCP.

Instead of spawning a full Python process with all dependencies for each
stdio session, this thin proxy forwards JSON-RPC messages between
stdin/stdout and the already-running HTTP MCP server inside the container.
Startup is near-instant since it only imports sys, json, and urllib.
"""

import json
import sys
import urllib.request
import urllib.error

MCP_URL = "http://localhost:8000/mcp"


def main() -> None:
    session_id: str | None = None

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        try:
            request_body = line.encode("utf-8")

            headers = {
                "Content-Type": "application/json",
                "Accept": "application/json, text/event-stream",
            }
            if session_id:
                headers["Mcp-Session-Id"] = session_id

            req = urllib.request.Request(
                MCP_URL,
                data=request_body,
                headers=headers,
                method="POST",
            )

            with urllib.request.urlopen(req, timeout=300) as resp:
                # Capture session ID from response headers
                sid = resp.headers.get("Mcp-Session-Id")
                if sid:
                    session_id = sid

                content_type = resp.headers.get("Content-Type", "")

                if "text/event-stream" in content_type:
                    # Parse SSE and extract JSON-RPC responses
                    for raw_line in resp:
                        decoded = raw_line.decode("utf-8").rstrip("\n\r")
                        if decoded.startswith("data: "):
                            data = decoded[6:]
                            if data:
                                sys.stdout.write(data + "\n")
                                sys.stdout.flush()
                else:
                    body = resp.read().decode("utf-8")
                    if body.strip():
                        sys.stdout.write(body.strip() + "\n")
                        sys.stdout.flush()

        except urllib.error.HTTPError as e:
            error_body = e.read().decode("utf-8", errors="replace")
            err_resp = {
                "jsonrpc": "2.0",
                "id": None,
                "error": {
                    "code": -32000,
                    "message": f"HTTP {e.code}: {error_body[:200]}",
                },
            }
            # Try to extract id from the request
            try:
                parsed = json.loads(line)
                err_resp["id"] = parsed.get("id")
            except Exception:
                pass
            sys.stdout.write(json.dumps(err_resp) + "\n")
            sys.stdout.flush()

        except Exception as e:
            err_resp = {
                "jsonrpc": "2.0",
                "id": None,
                "error": {"code": -32000, "message": str(e)[:200]},
            }
            try:
                parsed = json.loads(line)
                err_resp["id"] = parsed.get("id")
            except Exception:
                pass
            sys.stdout.write(json.dumps(err_resp) + "\n")
            sys.stdout.flush()


if __name__ == "__main__":
    main()
