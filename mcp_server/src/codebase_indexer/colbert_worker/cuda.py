"""CUDA availability probe for the ColBERT sidecar."""

from __future__ import annotations

import ctypes


def cuda_runtime_libraries_present() -> bool:
    """True when CUDA 12 / cuDNN 9 runtime .so files are loadable."""
    for lib in ("libcublasLt.so.12", "libcudnn.so.9", "libcudart.so.12"):
        try:
            ctypes.CDLL(lib)
        except OSError:
            return False
    return True


def cuda_available() -> bool:
    """True when ORT lists CUDA EP and required CUDA runtime libs are present."""
    try:
        import onnxruntime as ort

        return (
            "CUDAExecutionProvider" in ort.get_available_providers()
            and cuda_runtime_libraries_present()
        )
    except Exception:
        return False
