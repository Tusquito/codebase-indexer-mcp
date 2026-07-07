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


def tei_reachable(url: str) -> bool:
    try:
        urllib.request.urlopen(url.rstrip("/") + "/health", timeout=2)
        return True
    except Exception:
        return False


def colbert_reachable(url: str) -> bool:
    try:
        urllib.request.urlopen(url.rstrip("/") + "/health", timeout=2)
        return True
    except Exception:
        return False


def colbert_health(url: str) -> dict[str, object] | None:
    try:
        with urllib.request.urlopen(url.rstrip("/") + "/health", timeout=2) as resp:
            import json

            return json.loads(resp.read().decode("utf-8"))
    except Exception:
        return None
