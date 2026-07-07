# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Builder: compile/install dependencies into a self-contained venv.
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
COPY pyproject.toml uv.lock ./
COPY src/ ./src/
# Non-editable wheel build validates readme = "../README.md" → /README.md (outside /app context).
RUN printf '# codebase-indexer\n' > /README.md
ENV UV_PYTHON_PREFERENCE=only-system
RUN uv sync --frozen --no-dev --no-editable --python /usr/local/bin/python3.12 \
    && /app/.venv/bin/python -c "import codebase_indexer"

# ---------------------------------------------------------------------------
# Runtime: slim image with only what the server needs to run.
# Dense embedding runs in TEI; MCP keeps fastembed for sparse BM25 only.
# ---------------------------------------------------------------------------
FROM python:3.12-slim AS runtime

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    gosu \
    passwd \
    && rm -rf /var/lib/apt/lists/*

ENV PYTHONUNBUFFERED=1
ENV PYTHONPATH=/app/src
ENV PATH="/app/.venv/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
ENV LD_LIBRARY_PATH="/app/.venv/lib/python3.12/site-packages/onnxruntime/capi:${LD_LIBRARY_PATH:-}"

WORKDIR /app
COPY --from=builder /usr/local/bin/uv /usr/local/bin/uv
COPY --from=builder /app /app

COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN sed -i 's/\r$//' /usr/local/bin/docker-entrypoint.sh

RUN set -eux; \
    if existing="$(getent passwd 1000 | cut -d: -f1)" && [ -n "$existing" ]; then \
        userdel -r "$existing" 2>/dev/null || userdel "$existing"; \
    fi; \
    useradd -m -u 1000 mcpuser; \
    chown -R mcpuser /app; \
    chmod +x /usr/local/bin/docker-entrypoint.sh

EXPOSE 8000

HEALTHCHECK --interval=15s --timeout=5s --retries=3 \
  CMD curl -f http://localhost:8000/health || exit 1

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
CMD ["/app/.venv/bin/python", "-m", "codebase_indexer.main"]
