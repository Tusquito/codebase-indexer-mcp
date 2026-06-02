# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Builder stage: compile/install all dependencies into a self-contained venv.
# gcc/g++ live here only — they never reach the runtime image.
# ---------------------------------------------------------------------------
FROM python:3.12-slim AS builder

RUN apt-get update && apt-get install -y --no-install-recommends \
    gcc \
    g++ \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN curl -LsSf https://astral.sh/uv/install.sh | sh \
    && install -m 755 /root/.local/bin/uv /usr/local/bin/uv

WORKDIR /app
# Source is needed at sync time so the project (editable) install resolves.
COPY pyproject.toml uv.lock ./
COPY src/ ./src/
# --frozen fails loudly if uv.lock is out of date (no silent non-frozen fallback).
RUN uv sync --frozen --no-dev

# ---------------------------------------------------------------------------
# Runtime stage: slim image with only what the server needs to run.
# No compilers, no build tooling — smaller image and reduced attack surface.
# ---------------------------------------------------------------------------
FROM python:3.12-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    gosu \
    passwd \
    && rm -rf /var/lib/apt/lists/*

ENV PYTHONUNBUFFERED=1
# Use the prebuilt venv directly; UV_FROZEN keeps `uv run` (used by the stdio
# proxy via `docker exec`) from attempting any network lock resolution.
ENV PATH="/app/.venv/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
ENV UV_FROZEN=1

WORKDIR /app
# uv binary kept for the documented `docker exec ... uv run ...` stdio entrypoint.
COPY --from=builder /usr/local/bin/uv /usr/local/bin/uv
# Prebuilt venv + source (editable install references /app/src at this path).
COPY --from=builder /app /app

COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
# Windows checkouts may use CRLF; strip so the shebang interpreter resolves.
RUN sed -i 's/\r$//' /usr/local/bin/docker-entrypoint.sh

RUN /usr/sbin/useradd -m -u 1000 mcpuser \
    && chown -R mcpuser /app \
    && chmod +x /usr/local/bin/docker-entrypoint.sh

EXPOSE 8000

HEALTHCHECK --interval=15s --timeout=5s --retries=3 \
  CMD curl -f http://localhost:8000/health || exit 1

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
CMD ["/app/.venv/bin/python", "-m", "codebase_indexer.main"]
