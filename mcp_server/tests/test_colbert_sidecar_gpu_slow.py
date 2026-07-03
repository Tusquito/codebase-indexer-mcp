"""Optional GPU integration test for ColBERT sidecar (skipped without CUDA)."""

import pytest

from codebase_indexer.colbert_worker.cuda import cuda_available


@pytest.mark.gpu
@pytest.mark.slow
def test_colbert_sidecar_gpu_cuda_available():
    if not cuda_available():
        pytest.skip("CUDAExecutionProvider not available")
    assert cuda_available()
