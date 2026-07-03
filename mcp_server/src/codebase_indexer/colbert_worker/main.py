"""uvicorn entrypoint for the ColBERT HTTP sidecar."""

from __future__ import annotations

import uvicorn

from codebase_indexer.colbert_worker.app import create_app
from codebase_indexer.colbert_worker.settings import WorkerSettings


def main() -> None:
    settings = WorkerSettings()
    app = create_app(settings=settings)
    uvicorn.run(app, host=settings.host, port=settings.port, log_level="info")


if __name__ == "__main__":
    main()
