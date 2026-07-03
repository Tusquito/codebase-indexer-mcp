# Contributing

Thank you for contributing to the codebase-indexer MCP server. This guide covers local development setup, the CI workflow, and commit conventions.

## Prerequisites

- **Python 3.12** (matches CI and Docker images)
- **[uv](https://docs.astral.sh/uv/)** package manager
- A running **Qdrant** instance for integration tests (CI uses `http://localhost:6333`)

## Development setup

All Python code lives in `mcp_server/`. From the repository root:

```bash
cd mcp_server

# Install dependencies (including dev extras)
uv sync --extra dev

# Install Python 3.12 if uv does not already have it
uv python install 3.12
```

Copy `.env.example` to `.env` at the repo root and set the required variables before running Docker or local integration tests against Qdrant.

## CI workflow

CI is defined in `.github/workflows/ci.yml`. The **test** job runs from `mcp_server/` with Python 3.12 and a Qdrant service container. Reproduce locally:

```bash
cd mcp_server

# Lint
uv run ruff check .

# Type check (non-blocking in CI: failures do not fail the job)
uv run mypy src

# Tests (requires QDRANT_URL=http://localhost:6333 or similar)
uv run pytest -q
```

Set `QDRANT_URL` when running tests against a local or containerized Qdrant instance.

### Docker Compose integration (ADR pipeline)

Deploy the real stack and run integration checks from the **repository root**:

```bash
python scripts/run_compose_integration.py
python scripts/run_compose_integration.py --json    # machine-readable report
python scripts/run_compose_integration.py --keep    # leave stack up for debugging
```

This builds `mcp_server`, starts Qdrant + bundled Ollama + MCP via Compose, waits for health, runs `tests/test_storage_integration.py` against live Qdrant, and checks `http://127.0.0.1:8000/health`. Uses generated `.env.compose.integration` (does not overwrite your `.env`).

The ADR pipeline runs this via **`adr-integration-tester`** (step 3.5) before code review when the plan marks **Docker integration** as `required` or `auto` with deploy-touching changes.

### Integration smoke tests

Optional scripts under `mcp_server/scripts/` exercise live Qdrant + Ollama (skipped when services are unreachable):

```bash
cd mcp_server
python scripts/smoke_recommend_code.py   # recommend_code end-to-end
```

Requires an indexed collection (default `codebase-indexer-mcp`). Set `COLLECTION` to target another project. When running on the host against Docker, the script falls back to `localhost` for Ollama/Qdrant if `.env` uses in-compose hostnames.

## Commit conventions

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
type(scope): description
```

**Types:** `feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`, `perf`, `ci`, `build`

**Examples:**

- `feat(search): add language filter to symbols tool`
- `fix(indexer): skip symlinks during scan walk`
- `docs(readme): document HYBRID_SEARCH behavior`

Keep the subject line imperative, under ~50 characters when possible, and focused on *why* the change matters.

## Documentation sync policy

When you add, remove, or change an MCP tool (signature, behavior, or description string), update **both**:

1. `README.md` — tool tables and relevant sections
2. `.github/copilot-instructions.md` — tool table and Key conventions

Search-tool description changes should also stay aligned with `docs/SEARCH_BEHAVIOR.md`.

When adding discovery or orientation tools, also update `skill/codebase-indexer/SKILL.md` and `docs/ARCHITECTURE.md` MCP tools table.
