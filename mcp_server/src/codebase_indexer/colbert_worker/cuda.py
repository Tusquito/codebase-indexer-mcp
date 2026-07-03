"""CUDA availability probe for the ColBERT sidecar."""

from __future__ import annotations


def cuda_available() -> bool:
    try:
        import onnxruntime as ort

        return "CUDAExecutionProvider" in ort.get_available_providers()
    except Exception:
        return False
