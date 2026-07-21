"""Unit tests for BearerAuthMiddleware (main HTTP surface)."""

import hmac

from starlette.applications import Starlette
from starlette.middleware import Middleware
from starlette.requests import Request
from starlette.responses import JSONResponse
from starlette.routing import Route
from starlette.testclient import TestClient

from codebase_indexer.main import BearerAuthMiddleware


async def _ok(_request: Request) -> JSONResponse:
    return JSONResponse({"ok": True})


def _app(token: str) -> Starlette:
    return Starlette(
        routes=[
            Route("/health", _ok, methods=["GET"]),
            Route("/metrics", _ok, methods=["GET"]),
            Route("/mcp", _ok, methods=["POST"]),
        ],
        middleware=[Middleware(BearerAuthMiddleware, token=token)],
    )


def test_health_exempt_without_authorization():
    with TestClient(_app("secret-token")) as client:
        resp = client.get("/health")
    assert resp.status_code == 200
    assert resp.json() == {"ok": True}


def test_metrics_requires_bearer():
    with TestClient(_app("secret-token")) as client:
        assert client.get("/metrics").status_code == 401
        bad = client.get(
            "/metrics", headers={"Authorization": "Bearer wrong"}
        )
        assert bad.status_code == 401
        assert bad.json() == {"error": "unauthorized"}
        good = client.get(
            "/metrics", headers={"Authorization": "Bearer secret-token"}
        )
        assert good.status_code == 200


def test_mcp_requires_exact_bearer():
    with TestClient(_app("secret-token")) as client:
        assert client.post("/mcp").status_code == 401
        # Wrong scheme / missing Bearer prefix
        assert (
            client.post(
                "/mcp", headers={"Authorization": "secret-token"}
            ).status_code
            == 401
        )
        ok = client.post(
            "/mcp", headers={"Authorization": "Bearer secret-token"}
        )
        assert ok.status_code == 200


def test_compare_digest_rejects_length_mismatch():
    # Sanity: middleware uses compare_digest; short token must not match.
    expected = "Bearer secret-token"
    assert not hmac.compare_digest("Bearer x", expected)
