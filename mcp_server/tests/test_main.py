"""Tests for main.create_app wiring."""

from unittest.mock import MagicMock, patch

from codebase_indexer.config import Settings
from codebase_indexer.main import create_app


def test_create_app_omits_register_recommend_tool_when_disabled():
    settings = Settings(recommend_enabled=False, preload_models=False)
    mock_ctx = MagicMock()
    mock_ctx.settings = settings
    with (
        patch("codebase_indexer.main.AppContext.create", return_value=mock_ctx),
        patch("codebase_indexer.main.register_recommend_tool") as mock_register,
    ):
        create_app(settings, preload_models=False)
    mock_register.assert_not_called()
