"""Tests for language detection and .github/workflows scanning rules."""

import asyncio
import tempfile
from pathlib import Path

from codebase_indexer.indexer.languages import FILENAME_LANGUAGE_MAP
from codebase_indexer.indexer.scanner import (
    EXCLUDED_DIRS,
    _detect_language,
    _should_prune_dir,
    scan_files,
)


def test_github_not_in_excluded_dirs():
    assert ".github" not in EXCLUDED_DIRS


def test_migrations_in_excluded_dirs():
    assert "migrations" in EXCLUDED_DIRS


def test_should_prune_dir_venv_variants():
    excluded = EXCLUDED_DIRS
    assert _should_prune_dir(".venv", excluded)
    assert _should_prune_dir(".venv-bench", excluded)
    assert _should_prune_dir(".venv-train", excluded)
    assert not _should_prune_dir("src", excluded)


def test_scan_skips_venv_variant_dirs():
    async def _run() -> list[str]:
        paths: list[str] = []
        async for record in scan_files(workspace_path, readahead=4):
            paths.append(record.rel_path)
        return paths

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        (root / "src").mkdir()
        (root / "src" / "main.py").write_text("print('ok')\n", encoding="utf-8")
        for venv_name in (".venv", ".venv-bench", ".venv-train"):
            venv = root / "mcp_server" / venv_name
            venv.mkdir(parents=True)
            (venv / "lib.py").write_text("ignored\n", encoding="utf-8")

        workspace_path = str(root)
        paths = asyncio.run(_run())

    assert "src/main.py" in paths
    assert not any(".venv" in p for p in paths)


def test_detect_language_env_dotfiles():
    assert _detect_language(Path(".env.example")) == "properties"
    assert _detect_language(Path("src/.env.local")) == "properties"
    assert _detect_language(Path(".env")) == "properties"


def test_detect_language_gradle_kts_filenames():
    assert _detect_language(Path("build.gradle.kts")) == "groovy"
    assert _detect_language(Path("settings.gradle.kts")) == "groovy"
    assert _detect_language(Path("app.kts")) == "kotlin"


def test_filename_map_includes_env_and_gradle_kts():
    assert FILENAME_LANGUAGE_MAP[".env"] == "properties"
    assert FILENAME_LANGUAGE_MAP["build.gradle.kts"] == "groovy"


def test_scan_skips_files_directly_in_github_root():
    """Only .github/workflows/* is indexed, not dependabot.yml at .github/ root."""
    async def _run() -> list[str]:
        paths: list[str] = []
        async for record in scan_files(workspace_path, readahead=4):
            paths.append(record.rel_path)
        return paths

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        (root / ".github").mkdir()
        (root / ".github" / "dependabot.yml").write_text("version: 2\n", encoding="utf-8")
        (root / ".github" / "workflows").mkdir()
        (root / ".github" / "workflows" / "ci.yml").write_text(
            "name: CI\non: push\n", encoding="utf-8"
        )

        workspace_path = str(root)
        paths = asyncio.run(_run())

    assert ".github/workflows/ci.yml" in paths
    assert ".github/dependabot.yml" not in paths
