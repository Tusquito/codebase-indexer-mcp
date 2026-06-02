#!/usr/bin/env bash
# Trigger indexing of the CWD. Usage: ./scripts/index_local.sh [sub-path] [collection]
set -euo pipefail
PATH_ARG="${1:-/}"
COLLECTION="${2:-codebase}"
curl -s -X POST http://localhost:8000/mcp \
  -H "Content-Type: application/json" \
  -d "{
    \"jsonrpc\": \"2.0\",
    \"id\": 1,
    \"method\": \"tools/call\",
    \"params\": {
      \"name\": \"index_codebase\",
      \"arguments\": { \"path\": \"$PATH_ARG\", \"collection\": \"$COLLECTION\" }
    }
  }" | python3 -m json.tool
