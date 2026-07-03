"""Index-time Neo4j graph writer (ADR 0002 Phase 1)."""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path

import structlog

from codebase_indexer.indexer.chunker import Chunk, extract_imported_names
from codebase_indexer.tools.build_deps import (
    extract_build_deps,
    is_build_manifest,
    match_deps_to_collections,
)
from codebase_indexer.tools.cross_references import UrlExtractors

log = structlog.get_logger()

_HTTP_METHOD_PATTERN = re.compile(
    r"@(?:Get|Post|Put|Delete|Patch|Request)Mapping|"
    r"\[(?:Http(?:Get|Post|Put|Delete|Patch))\]|"
    r"\.Map(Get|Post|Put|Delete|Patch)\(|"
    r"(?:app|router)\.(get|post|put|delete|patch)\(",
    re.IGNORECASE,
)
_CONFIG_FILE_PATTERN = re.compile(
    r"\.(ya?ml|json|properties|env|config)$", re.IGNORECASE
)


def symbol_qualified_name(collection: str, rel_path: str, symbol_name: str) -> str:
    """Stable Symbol key: {collection}:{rel_path}::{symbol_name}."""
    return f"{collection}:{rel_path}::{symbol_name}"


def import_qualified_name(collection: str, import_name: str) -> str:
    """Qualified name for an unresolved import target."""
    return f"{collection}::import::{import_name}"


def callee_qualified_name(collection: str, callee: str) -> str:
    """Qualified name for a best-effort callee symbol."""
    return f"{collection}::callee::{callee}"


def artifact_key(group: str, name: str, ecosystem: str) -> str:
    """Stable Artifact key across collections."""
    return f"{ecosystem}:{group}:{name}" if group else f"{ecosystem}:{name}"


def infer_http_method(content: str) -> str:
    """Best-effort HTTP method inference from endpoint definition content."""
    match = _HTTP_METHOD_PATTERN.search(content)
    if not match:
        return ""
    token = match.group(0).lower()
    for method in ("get", "post", "put", "delete", "patch"):
        if method in token:
            return method.upper()
    return ""


def extract_file_import_names(content: str, language: str) -> list[str]:
    """Collect imported symbol names from raw file content."""
    seen: set[str] = set()
    names: list[str] = []
    for line in content.splitlines():
        imported = extract_imported_names(line, language)
        if imported is None or not imported:
            continue
        for name in imported:
            if name not in seen:
                seen.add(name)
                names.append(name)
    return names


@dataclass
class GraphBatch:
    """Structured graph upsert payload for one flush batch."""

    collection: str
    schema_version: int = 1
    collection_props: bool = True
    files: list[dict] = field(default_factory=list)
    chunks: list[dict] = field(default_factory=list)
    defines: list[dict] = field(default_factory=list)
    calls: list[dict] = field(default_factory=list)
    imports: list[dict] = field(default_factory=list)
    endpoints: list[dict] = field(default_factory=list)
    declares_endpoint: list[dict] = field(default_factory=list)
    http_calls: list[dict] = field(default_factory=list)
    configures: list[dict] = field(default_factory=list)
    build_deps: list[dict] = field(default_factory=list)
    resolves_to: list[dict] = field(default_factory=list)


def build_graph_batch(
    *,
    collection: str,
    chunks: list[Chunk],
    url_extractors: UrlExtractors,
    workspace_path: str,
    collection_names: list[str],
    schema_version: int = 1,
) -> GraphBatch:
    """Build a graph batch from indexed chunks (grouped by file)."""
    batch = GraphBatch(collection=collection, schema_version=schema_version)
    if not chunks:
        return batch

    by_file: dict[str, list[Chunk]] = {}
    for chunk in chunks:
        by_file.setdefault(chunk.rel_path, []).append(chunk)

    seen_files: set[str] = set()
    seen_build_keys: set[str] = set()

    for rel_path, file_chunks in by_file.items():
        first = file_chunks[0]
        if rel_path not in seen_files:
            seen_files.add(rel_path)
            batch.files.append(
                {
                    "rel_path": rel_path,
                    "language": first.language,
                    "sha256": first.file_sha256,
                }
            )

        for chunk in file_chunks:
            batch.chunks.append(
                {
                    "chunk_id": chunk.chunk_id,
                    "rel_path": rel_path,
                    "start_line": chunk.start_line,
                    "end_line": chunk.end_line,
                }
            )

            if chunk.symbol_name:
                qn = symbol_qualified_name(collection, rel_path, chunk.symbol_name)
                batch.defines.append(
                    {
                        "chunk_id": chunk.chunk_id,
                        "qualified_name": qn,
                        "name": chunk.symbol_name,
                        "kind": chunk.symbol_type,
                    }
                )

            for callee in chunk.callees:
                batch.calls.append(
                    {
                        "chunk_id": chunk.chunk_id,
                        "qualified_name": callee_qualified_name(collection, callee),
                        "name": callee,
                    }
                )

            for route in url_extractors.route_paths(chunk.content, rel_path):
                method = infer_http_method(chunk.content)
                batch.endpoints.append({"path": route, "method": method})
                batch.declares_endpoint.append(
                    {"chunk_id": chunk.chunk_id, "path": route}
                )

            for path in url_extractors.code_urls(chunk.content):
                batch.endpoints.append({"path": path, "method": ""})
                batch.http_calls.append({"chunk_id": chunk.chunk_id, "path": path})

            if _CONFIG_FILE_PATTERN.search(rel_path):
                config_paths, _base_urls = url_extractors.config_urls(chunk.content)
                for path in config_paths:
                    batch.endpoints.append({"path": path, "method": ""})
                    batch.configures.append({"chunk_id": chunk.chunk_id, "path": path})

        file_content = _read_file_content(workspace_path, rel_path)
        if file_content is None:
            file_content = "\n".join(c.content for c in file_chunks)

        for import_name in extract_file_import_names(file_content, first.language):
            batch.imports.append(
                {
                    "rel_path": rel_path,
                    "qualified_name": import_qualified_name(collection, import_name),
                    "name": import_name,
                }
            )

        if is_build_manifest(rel_path):
            manifest_content = _read_file_content(workspace_path, rel_path) or file_content
            deps = extract_build_deps(manifest_content, rel_path)
            matches = match_deps_to_collections(
                deps,
                collection_names,
                self_collection=collection,
            )
            match_by_artifact: dict[str, str] = {
                m["artifact"]: m["matched_collection"] for m in matches
            }
            for dep in deps:
                key = artifact_key(dep.group, dep.artifact, dep.ecosystem)
                if key in seen_build_keys:
                    continue
                seen_build_keys.add(key)
                batch.build_deps.append(
                    {
                        "key": key,
                        "name": dep.artifact,
                        "group": dep.group,
                        "ecosystem": dep.ecosystem,
                        "version": dep.version,
                        "scope": dep.scope,
                    }
                )
                target = match_by_artifact.get(dep.artifact)
                if target:
                    batch.resolves_to.append(
                        {
                            "artifact_key": key,
                            "target_collection": target,
                        }
                    )

    return batch


def _read_file_content(workspace_path: str, rel_path: str) -> str | None:
    """Re-read manifest/source from disk for full-file extractors."""
    path = Path(workspace_path) / rel_path.replace("\\", "/")
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        log.warning("graph_writer_read_failed", path=rel_path, error=str(exc))
        return None


async def write_chunks_to_graph(
    graph_storage,
    *,
    collection: str,
    chunks: list[Chunk],
    url_extractors: UrlExtractors,
    workspace_path: str,
    collection_names: list[str],
    schema_version: int = 1,
) -> None:
    """Build and persist a graph batch (no-op when storage disabled)."""
    if graph_storage is None or not graph_storage.enabled:
        return

    batch = build_graph_batch(
        collection=collection,
        chunks=chunks,
        url_extractors=url_extractors,
        workspace_path=workspace_path,
        collection_names=collection_names,
        schema_version=schema_version,
    )
    await graph_storage.write_batch(batch)
