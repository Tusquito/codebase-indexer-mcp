# Fine-tuning / train pipeline (deferred after ADR 0030 Phase 7)

The Python MCP runtime was removed. Scripts under ``benchmarks/train/`` that
imported ``codebase_indexer`` are **deferred** pending an MCP-HTTP / offline
port. Quality validation uses:

```bash
cd benchmarks
uv sync --extra dev --extra benchmark
uv run python -m benchmarks.eval_retrieval --mcp-url http://127.0.0.1:8000/mcp \
  --compare benchmarks/fixtures/eval_baseline.json
```

Schema helpers (``_schema``, ``_split``) remain importable for unit tests.
