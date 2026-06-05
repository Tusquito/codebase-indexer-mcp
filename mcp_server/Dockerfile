# syntax=docker/dockerfile:1

ARG EMBED_DEVICE=cpu

# ---------------------------------------------------------------------------
# Builder stages: compile/install all dependencies into a self-contained venv.
# gcc/g++ live here only — they never reach the runtime image.
# ---------------------------------------------------------------------------
FROM python:3.12-slim AS builder-cpu

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
RUN uv sync --frozen --no-dev

FROM nvidia/cuda:12.8.1-devel-ubuntu24.04 AS builder-cuda

RUN apt-get update && apt-get install -y --no-install-recommends \
    python3.12 \
    python3.12-venv \
    python3.12-dev \
    gcc \
    g++ \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python3 \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python

RUN curl -LsSf https://astral.sh/uv/install.sh | sh \
    && install -m 755 /root/.local/bin/uv /usr/local/bin/uv

WORKDIR /app
COPY pyproject.toml uv.lock ./
COPY src/ ./src/
# Swap the CPU fastembed/onnxruntime for the GPU build. Pin fastembed-gpu to the
# same version resolved in uv.lock for the CPU fastembed so GPU images stay
# reproducible and aligned with the locked onnxruntime CUDA/cuDNN expectations.
RUN uv sync --frozen --no-dev \
    && uv pip uninstall fastembed onnxruntime \
    && uv pip install fastembed-gpu==0.8.0 onnxruntime-gpu==1.26.0
RUN /app/.venv/bin/python -c "import onnxruntime; assert onnxruntime.__version__ == '1.26.0'"

FROM builder-${EMBED_DEVICE} AS builder
ARG EMBED_DEVICE
RUN test "$EMBED_DEVICE" = "cpu" -o "$EMBED_DEVICE" = "cuda" \
    || (echo "ERROR: EMBED_DEVICE must be 'cpu' or 'cuda', got '$EMBED_DEVICE'" >&2 && exit 1)

# ---------------------------------------------------------------------------
# Runtime stages: slim image with only what the server needs to run.
# No compilers, no build tooling — smaller image and reduced attack surface.
# ---------------------------------------------------------------------------
FROM python:3.12-slim AS runtime-cpu

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    gosu \
    passwd \
    && rm -rf /var/lib/apt/lists/*

# cuDNN variant: onnxruntime-gpu (pulled by fastembed-gpu) requires cuDNN 9.x at
# runtime. The plain -runtime- image ships CUDA libs but not cuDNN, so the
# CUDAExecutionProvider would fail to load. Use the -cudnn-runtime- tag.
FROM nvidia/cuda:12.8.1-cudnn-runtime-ubuntu24.04 AS runtime-cuda

ENV NVIDIA_VISIBLE_DEVICES=all
ENV NVIDIA_DRIVER_CAPABILITIES=compute,utility

RUN apt-get update && apt-get install -y --no-install-recommends \
    python3.12 \
    python3.12-venv \
    curl \
    ca-certificates \
    gosu \
    passwd \
    && rm -rf /var/lib/apt/lists/* \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python3 \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python

FROM runtime-${EMBED_DEVICE} AS runtime

ENV PYTHONUNBUFFERED=1
ENV PATH="/app/.venv/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"

WORKDIR /app
# uv binary kept for the documented `docker exec ... uv run ...` stdio entrypoint.
COPY --from=builder /usr/local/bin/uv /usr/local/bin/uv
# Prebuilt venv + source (editable install references /app/src at this path).
COPY --from=builder /app /app

COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
# Windows checkouts may use CRLF; strip so the shebang interpreter resolves.
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
