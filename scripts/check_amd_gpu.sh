#!/usr/bin/env bash
# Verify the AMD ROCm/MIGraphX GPU execution provider is available inside the
# built container. Run this on the AMD host (native Linux or Windows+WSL2).
#
# All in-container Python checks target /app/.venv/bin/python (bypassing the
# entrypoint, which resets PATH and drops the project venv).
#
# Usage:
#   ./scripts/check_amd_gpu.sh [image] [mode]
#     image  Docker image to test (default: codebase-indexer:rocm)
#     mode   Device passthrough: "native" (default) or "wsl"
#
# Examples:
#   ./scripts/check_amd_gpu.sh
#   ./scripts/check_amd_gpu.sh codebase-indexer:rocm native
#   ./scripts/check_amd_gpu.sh codebase-indexer:rocm-wsl wsl
set -uo pipefail

VENV_PY=/app/.venv/bin/python
IMAGE="${1:-codebase-indexer:rocm}"
MODE="${2:-native}"

case "$MODE" in
  native)
    DEVICE_ARGS=(--device /dev/kfd --device /dev/dri --group-add video --group-add render)
    ;;
  wsl)
    # WSL2 routes the GPU through /dev/dxg (ROCDXG). librocdxg/libdxcore are
    # bind-mounted by docker-compose.amd.wsl2.yml; replicate the essentials here.
    DEVICE_ARGS=(--device /dev/dxg)
    [ -e /usr/lib/wsl/lib/libdxcore.so ] && DEVICE_ARGS+=(-v /usr/lib/wsl/lib/libdxcore.so:/usr/lib/libdxcore.so:ro)
    [ -e /opt/rocm/lib/librocdxg.so ] && DEVICE_ARGS+=(-v /opt/rocm/lib/librocdxg.so:/usr/lib/librocdxg.so:ro)
    ;;
  *)
    echo "ERROR: mode must be 'native' or 'wsl', got '$MODE'" >&2
    exit 1
    ;;
esac

SOPATH="/app/.venv/lib/python3.12/site-packages/onnxruntime/capi/libonnxruntime_providers_migraphx.so"
PASS=0

hr() { printf '%s\n' "------------------------------------------------------------"; }
run_python() {
  echo "+ docker run --rm --entrypoint $VENV_PY ${DEVICE_ARGS[*]} $IMAGE -c ..."
  docker run --rm --entrypoint "$VENV_PY" "${DEVICE_ARGS[@]}" "$IMAGE" -c "$1"
}
run_shell() {
  echo "+ docker run --rm --entrypoint \"\" ${DEVICE_ARGS[*]} $IMAGE bash -lc ..."
  docker run --rm --entrypoint "" "${DEVICE_ARGS[@]}" "$IMAGE" bash -lc "$1"
}

echo "Image: $IMAGE"
echo "Mode:  $MODE"
hr

echo "[1/5] ONNX Runtime available execution providers (venv python)"
PROVIDERS="$(run_python "import onnxruntime as ort; print(' '.join(ort.get_available_providers()))" 2>&1)"
echo "$PROVIDERS"
if echo "$PROVIDERS" | grep -qE "MIGraphXExecutionProvider|ROCMExecutionProvider"; then
  echo "RESULT: GPU execution provider IS available."
  PASS=1
else
  echo "RESULT: No GPU EP found — embedding will run on CPU. See checks 3–5 below."
fi
hr

echo "[2/5] Venv packages (fastembed, onnxruntime)"
run_python "import fastembed, onnxruntime; print('fastembed', fastembed.__version__, '| ort', onnxruntime.__version__, '| providers', onnxruntime.get_available_providers())"
hr

echo "[3/5] ROCm sees the GPU (rocminfo)"
run_shell 'rocminfo 2>/dev/null | grep -iE "Name:|gfx" || echo "rocminfo unavailable or no agents"'
hr

echo "[4/5] GPU status (rocm-smi)"
run_shell '/opt/rocm/bin/rocm-smi 2>/dev/null || echo "rocm-smi unavailable"'
hr

echo "[5/5] MIGraphX provider .so resolves its shared libraries (ldd)"
MISSING="$(run_shell "ldd $SOPATH 2>/dev/null | grep -i 'not found'" 2>&1)"
if [ -z "$MISSING" ]; then
  echo "RESULT: all shared libraries resolved (no 'not found')."
else
  echo "RESULT: missing libraries detected:"
  echo "$MISSING"
fi
hr

if [ "$PASS" -eq 1 ]; then
  echo "OVERALL: PASS — AMD GPU execution provider is available."
  exit 0
fi
echo "OVERALL: GPU EP NOT available (CPU fallback). Check GPU passthrough (mode),"
echo "         that the gfx arch is supported (try HSA_OVERRIDE_GFX_VERSION for"
echo "         gfx1151), and the [5/5] ldd output above for missing libraries."
exit 1
