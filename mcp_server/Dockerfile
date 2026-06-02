FROM python:3.12-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
    git \
    gcc \
    g++ \
    curl \
    && rm -rf /var/lib/apt/lists/*

RUN curl -LsSf https://astral.sh/uv/install.sh | sh
ENV PYTHONUNBUFFERED=1
ENV PATH="/root/.cargo/bin:/root/.local/bin:$PATH"

WORKDIR /app
COPY pyproject.toml uv.lock* ./
RUN uv sync --frozen --no-dev || uv sync --no-dev

COPY src/ ./src/

RUN useradd -m -u 1000 mcpuser && chown -R mcpuser /app
USER mcpuser

EXPOSE 8000

HEALTHCHECK --interval=15s --timeout=5s --retries=3 \
  CMD curl -f http://localhost:8000/health || exit 1

ENTRYPOINT ["uv", "run", "python", "-m", "codebase_indexer.main"]
