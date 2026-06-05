#!/usr/bin/env bash
# Trigger indexing of a project. Usage: ./scripts/index_local.sh <project-folder> [collection]
set -euo pipefail
if [ $# -lt 1 ]; then
  echo "Usage: $0 <project-folder> [collection]" >&2
  echo "  project-folder: basename of a directory under WORKSPACE_ROOT (never '/')" >&2
  exit 1
fi
PATH_ARG="$1"
COLLECTION="${2:-$PATH_ARG}"
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
