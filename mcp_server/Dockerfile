# syntax=docker/dockerfile:1

ARG EMBED_DEVICE=cpu
ARG ROCM_VARIANT=native

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
COPY pyproject.toml uv.lock ./
COPY src/ ./src/
# Non-editable wheel build validates readme = "../README.md" → /README.md (outside /app context).
RUN printf '# codebase-indexer\n' > /README.md
ENV UV_PYTHON_PREFERENCE=only-system
RUN uv sync --frozen --no-dev --no-editable --python /usr/local/bin/python3.12 \
    && /app/.venv/bin/python -c "import codebase_indexer"

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
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python3.12 \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python3 \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python

RUN curl -LsSf https://astral.sh/uv/install.sh | sh \
    && install -m 755 /root/.local/bin/uv /usr/local/bin/uv

WORKDIR /app
COPY pyproject.toml uv.lock ./
COPY src/ ./src/
RUN printf '# codebase-indexer\n' > /README.md
# Swap the CPU fastembed/onnxruntime for the GPU build. Pin fastembed-gpu to the
# same version resolved in uv.lock for the CPU fastembed so GPU images stay
# reproducible and aligned with the locked onnxruntime CUDA/cuDNN expectations.
ENV UV_PYTHON_PREFERENCE=only-system
RUN uv sync --frozen --no-dev --no-editable --python /usr/local/bin/python3.12 \
    && uv pip uninstall fastembed onnxruntime \
    && uv pip install fastembed-gpu==0.8.0 onnxruntime-gpu==1.26.0 \
    && /app/.venv/bin/python -c "import onnxruntime; assert onnxruntime.__version__ == '1.26.0'" \
    && /app/.venv/bin/python -c "import codebase_indexer"

# ROCm images are large (~3 GB+ base). Pin base tag and onnxruntime wheel to the same
# ROCm release (see repo.radeon.com/rocm/manylinux/rocm-rel-<ver>/).
# ROCM_VARIANT=wsl targets the ROCDXG /dev/dxg passthrough path on Windows+WSL2.
FROM rocm/dev-ubuntu-24.04:6.4.4 AS builder-rocm-native

RUN apt-get update && apt-get install -y --no-install-recommends \
    python3.12 \
    python3.12-venv \
    python3.12-dev \
    gcc \
    g++ \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python3.12 \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python3 \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python

RUN curl -LsSf https://astral.sh/uv/install.sh | sh \
    && install -m 755 /root/.local/bin/uv /usr/local/bin/uv

WORKDIR /app
COPY pyproject.toml uv.lock ./
COPY src/ ./src/
RUN printf '# codebase-indexer\n' > /README.md
# Keep plain fastembed (not fastembed-gpu). Swap CPU onnxruntime for onnxruntime-rocm.
ENV UV_PYTHON_PREFERENCE=only-system
RUN uv sync --frozen --no-dev --no-editable --python /usr/local/bin/python3.12 \
    && uv pip uninstall onnxruntime onnxruntime-gpu fastembed-gpu 2>/dev/null || true \
    && uv pip install onnxruntime-rocm==1.21.0 \
        -f https://repo.radeon.com/rocm/manylinux/rocm-rel-6.4.4/ \
    && /app/.venv/bin/python -c "import onnxruntime; assert onnxruntime.__version__ == '1.21.0'" \
    && /app/.venv/bin/python -c "import codebase_indexer"

FROM rocm/dev-ubuntu-24.04:7.2.1 AS builder-rocm-wsl

RUN apt-get update && apt-get install -y --no-install-recommends \
    python3.12 \
    python3.12-venv \
    python3.12-dev \
    gcc \
    g++ \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python3.12 \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python3 \
    && ln -sf /usr/bin/python3.12 /usr/local/bin/python

RUN curl -LsSf https://astral.sh/uv/install.sh | sh \
    && install -m 755 /root/.local/bin/uv /usr/local/bin/uv

WORKDIR /app
COPY pyproject.toml uv.lock ./
COPY src/ ./src/
RUN printf '# codebase-indexer\n' > /README.md
# ROCm 7.2+ ships onnxruntime_migraphx (not onnxruntime-rocm). numpy<2 required by wheel.
ENV UV_PYTHON_PREFERENCE=only-system
RUN uv sync --frozen --no-dev --no-editable --python /usr/local/bin/python3.12 \
    && uv pip uninstall onnxruntime onnxruntime-gpu fastembed-gpu onnxruntime-rocm 2>/dev/null || true \
    && uv pip install "numpy<2" onnxruntime_migraphx==1.23.2 \
        -f https://repo.radeon.com/rocm/manylinux/rocm-rel-7.2.1/ \
    && /app/.venv/bin/python -c "import onnxruntime; assert onnxruntime.__version__ == '1.23.2'" \
    && /app/.venv/bin/python -c "import codebase_indexer"

FROM builder-rocm-${ROCM_VARIANT} AS builder-rocm

FROM builder-${EMBED_DEVICE} AS builder
ARG EMBED_DEVICE
ARG ROCM_VARIANT
RUN test "$EMBED_DEVICE" = "cpu" -o "$EMBED_DEVICE" = "cuda" -o "$EMBED_DEVICE" = "rocm" \
    || (echo "ERROR: EMBED_DEVICE must be 'cpu', 'cuda', or 'rocm', got '$EMBED_DEVICE'" >&2 && exit 1)
RUN test "$ROCM_VARIANT" = "native" -o "$ROCM_VARIANT" = "wsl" \
    || (echo "ERROR: ROCM_VARIANT must be 'native' or 'wsl', got '$ROCM_VARIANT'" >&2 && exit 1)

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

FROM rocm/dev-ubuntu-24.04:6.4.4 AS runtime-rocm-native

ENV HSA_ENABLE_DXG_DETECTION=1
ENV LD_LIBRARY_PATH="/opt/rocm/lib:${LD_LIBRARY_PATH:-}"

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

FROM rocm/dev-ubuntu-24.04:7.2.1 AS runtime-rocm-wsl

ENV HSA_ENABLE_DXG_DETECTION=1
ENV LD_LIBRARY_PATH="/opt/rocm/lib:${LD_LIBRARY_PATH:-}"

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

FROM runtime-rocm-${ROCM_VARIANT} AS runtime-rocm

FROM runtime-${EMBED_DEVICE} AS runtime

ENV PYTHONUNBUFFERED=1
ENV PYTHONPATH=/app/src
ENV PATH="/app/.venv/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"

WORKDIR /app
# uv binary kept for the documented `docker exec ... uv run ...` stdio entrypoint.
COPY --from=builder /usr/local/bin/uv /usr/local/bin/uv
# Prebuilt venv + source.
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
