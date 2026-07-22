# Contributing

Thank you for contributing to the codebase-indexer MCP server. This guide covers local development setup, the CI workflow, and commit conventions.

## Prerequisites

- **.NET SDK 10** (see `global.json`) — primary runtime
- **Python 3.12** + **[uv](https://docs.astral.sh/uv/)** — only for `benchmarks/` eval tooling and tracker scripts
- **Docker** for Aspire compose integration
- A running **Qdrant** instance for live eval (CI may use a service container)

## Development setup

### .NET (runtime)

```bash
dotnet test CodebaseIndexer.slnx
dotnet run --project src/CodebaseIndexer.AppHost
```

### Python (benchmarks / tracker only)

```bash
cd benchmarks
uv sync --extra dev --extra benchmark
uv run pytest -q
```

From repo root:

```bash
uv run --directory benchmarks python scripts/render_adr_tracker.py --check
```

Copy `.env.example` to `.env` at the repo root and set required compose variables before `docker compose $(python scripts/aspire_compose.py) up`.

## CI workflow

CI is defined in `.github/workflows/ci.yml`:

- **dotnet-test** (blocking) — `dotnet test CodebaseIndexer.slnx`
- **python-devtools** (blocking) — benchmarks pytest + ADR tracker `--check`
- **aspire-integration** (non-blocking in GH) — `scripts/run_compose_integration.py` with quality validation

Reproduce compose smoke locally:

```bash
ACCELERATOR=cpu python scripts/run_compose_integration.py --json --quality-validation --quality-threshold 0
```

## Commit conventions

Use [Conventional Commits](https://www.conventionalcommits.org/): `feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`, `perf`, `ci`, `build`. Keep subjects under 50 characters when practical.
