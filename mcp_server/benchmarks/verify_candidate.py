"""tei_health + tei_embed_smoke verification for ADR 0026 candidates.

Confirms that the TEI sidecar currently serving a candidate answers ``/health``
and returns an embedding at the candidate's *registered* dimension via
``POST /v1/embeddings``. This is the Phase 3 pre-flight the bake-off (Phase 4)
runs before scoring any candidate, catching a mis-served model or a dimension
mismatch before it silently corrupts a run.

TEI serves exactly one model per container (set through ``DENSE_EMBED_MODEL`` /
``TEI_*`` per ADR 0025), so this verifies the *single* candidate the running TEI
is configured for. Usage from ``mcp_server/``::

    uv run python -m benchmarks.verify_candidate --model Alibaba-NLP/gte-modernbert-base
    uv run python -m benchmarks.verify_candidate --all   # every included candidate
"""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass

import httpx

from benchmarks._settings import load_settings_for_candidate
from benchmarks.candidates import Candidate, load_registry
from codebase_indexer.config import tei_embed_dimensions

SMOKE_INPUT = "def add(a, b):\n    return a + b\n"


@dataclass
class VerifyResult:
    model: str
    tei_health: bool
    tei_embed_smoke: bool
    expected_dim: int
    observed_dim: int | None
    error: str | None = None

    @property
    def ok(self) -> bool:
        return (
            self.tei_health
            and self.tei_embed_smoke
            and self.observed_dim == self.expected_dim
            and self.error is None
        )

    def render(self) -> str:
        status = "PASS" if self.ok else "FAIL"
        dim = f"{self.observed_dim}/{self.expected_dim}"
        line = (
            f"[{status}] {self.model} health={self.tei_health} "
            f"smoke={self.tei_embed_smoke} dim(observed/expected)={dim}"
        )
        if self.error:
            line += f" error={self.error}"
        return line


def _expected_dim(candidate: Candidate) -> int:
    """Registered serving dimension (MRL vector_size when below native)."""
    return candidate.vector_size


def verify_candidate(
    candidate: Candidate,
    *,
    tei_url: str,
    timeout: float = 30.0,
) -> VerifyResult:
    """Run ``tei_health`` + ``tei_embed_smoke`` against the running TEI."""
    expected = _expected_dim(candidate)
    base = tei_url.rstrip("/")
    # MRL candidates served below native dim need the OpenAI `dimensions` param.
    mrl_dim = tei_embed_dimensions(candidate.model, candidate.vector_size)

    try:
        with httpx.Client(base_url=base, timeout=httpx.Timeout(timeout)) as client:
            health = client.get("/health")
            tei_health = health.status_code == 200
            if not tei_health:
                return VerifyResult(
                    candidate.model, False, False, expected, None,
                    error=f"/health -> HTTP {health.status_code}",
                )

            payload: dict[str, object] = {
                "model": candidate.model,
                "input": SMOKE_INPUT,
            }
            if mrl_dim is not None:
                payload["dimensions"] = mrl_dim
            resp = client.post("/v1/embeddings", json=payload)
            if resp.status_code != 200:
                return VerifyResult(
                    candidate.model, True, False, expected, None,
                    error=f"/v1/embeddings -> HTTP {resp.status_code}",
                )
            data = resp.json()
            items = data.get("data") or []
            if not items:
                return VerifyResult(
                    candidate.model, True, False, expected, None,
                    error="no embedding data in response",
                )
            observed = len(items[0].get("embedding", []))
            return VerifyResult(
                candidate.model,
                tei_health=True,
                tei_embed_smoke=True,
                expected_dim=expected,
                observed_dim=observed,
            )
    except (httpx.HTTPError, ValueError, KeyError) as exc:
        return VerifyResult(
            candidate.model, False, False, expected, None, error=str(exc)
        )


def _resolve_tei_url(candidate: Candidate) -> str:
    return load_settings_for_candidate(candidate).tei_url


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--model", help="Candidate model id to verify")
    group.add_argument(
        "--all",
        action="store_true",
        help="Verify every included candidate (expects TEI to be serving each)",
    )
    parser.add_argument("--tei-url", default=None, help="Override TEI base URL")
    args = parser.parse_args(argv)

    registry = load_registry()
    if args.all:
        targets = list(registry.included)
    else:
        try:
            targets = [registry.by_model(args.model)]
        except KeyError:
            print(f"unknown candidate: {args.model!r}", file=sys.stderr)
            return 2

    exit_code = 0
    for candidate in targets:
        tei_url = args.tei_url or _resolve_tei_url(candidate)
        result = verify_candidate(candidate, tei_url=tei_url)
        print(result.render())
        if not result.ok:
            exit_code = 1
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
