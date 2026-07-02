"""Shared reachability checks for benchmark harnesses."""

from __future__ import annotations

import urllib.request


def qdrant_reachable(url: str) -> bool:
    for suffix in ("/healthz", "/"):
        try:
            urllib.request.urlopen(url.rstrip("/") + suffix, timeout=2)
            return True
        except Exception:
            continue
    return False


def ollama_reachable(url: str) -> bool:
    try:
        urllib.request.urlopen(url.rstrip("/") + "/api/tags", timeout=2)
        return True
    except Exception:
        return False
