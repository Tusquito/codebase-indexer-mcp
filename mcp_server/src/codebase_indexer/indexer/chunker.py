# src/codebase_indexer/indexer/chunker.py
"""AST-based code chunking using Tree-sitter, with sliding window fallback."""

import hashlib
import re
from dataclasses import dataclass
from pathlib import PurePosixPath

import structlog
from tree_sitter import Language, Parser, Node

from codebase_indexer.indexer.languages import AST_LANGUAGE_SPECS, SLIDING_WINDOW_LANGUAGES

log = structlog.get_logger()

# Built once at import time from the central language registry. Grammars are
# loaded lazily by each spec's loader (so only AST languages pull tree-sitter).
LANGUAGES: dict[str, Language] = {
    spec.name: spec.grammar_loader() for spec in AST_LANGUAGE_SPECS  # type: ignore[misc]
}

# One reusable Parser per language, built once at import time. Constructing a
# Parser per file is wasteful; Tree-sitter parsers are reusable across parses
# (each call to parse() produces an independent tree).
_PARSERS: dict[str, Parser] = {lang: Parser(obj) for lang, obj in LANGUAGES.items()}

# Node types to extract per language, sourced from the registry.
EXTRACT_NODE_TYPES: dict[str, set[str]] = {
    spec.name: set(spec.node_types) for spec in AST_LANGUAGE_SPECS
}


@dataclass
class Chunk:
    chunk_id: str
    content: str
    rel_path: str
    language: str
    start_line: int
    end_line: int
    symbol_name: str | None
    symbol_type: str
    file_sha256: str
    file_mtime: float = 0.0


def _make_chunk_id(rel_path: str, start_line: int) -> str:
    """Deterministic chunk ID from path and start line."""
    raw = f"{rel_path}:{start_line}"
    return hashlib.sha256(raw.encode()).hexdigest()


def _extract_symbol_name(node: Node) -> str | None:
    """Try to extract a symbol name from a tree-sitter node."""
    for child in node.children:
        if child.type == "object_reference":
            parts = []
            for sub in child.children:
                if sub.type == "identifier" and sub.text:
                    parts.append(sub.text.decode("utf-8", errors="replace"))
            if parts:
                return ".".join(parts)
        if child.type in ("identifier", "name", "property_identifier", "type_identifier"):
            return child.text.decode("utf-8", errors="replace") if child.text is not None else None
        # For decorated_definition / export_statement, look deeper
        if child.type in ("function_definition", "class_definition", "function_declaration",
                          "class_declaration", "method_declaration"):
            return _extract_symbol_name(child)
    return None


def _classify_symbol_type(node_type: str) -> str:
    """Classify the AST node type into a semantic category."""
    if node_type == "create_table":
        return "table"
    if node_type == "create_procedure":
        return "procedure"
    if node_type == "create_function":
        return "function"
    if node_type == "create_view":
        return "view"
    if node_type == "create_trigger":
        return "trigger"
    if node_type == "create_type":
        return "type"
    if node_type == "create_index":
        return "index"
    if "class" in node_type or "struct" in node_type or "enum" in node_type or "interface" in node_type:
        return "class"
    if "method" in node_type or "constructor" in node_type:
        return "method"
    if "function" in node_type or "arrow_function" in node_type:
        return "function"
    return "other"


def _chunk_symbol_metadata(
    node: Node,
    fallback_name: str | None,
    fallback_type: str,
) -> tuple[str | None, str]:
    """Resolve symbol name/type for a chunk, preferring the node over parent fallbacks."""
    own_name = _extract_symbol_name(node)
    own_type = _classify_symbol_type(node.type)
    name = own_name if own_name is not None else fallback_name
    if own_type != "other":
        symbol_type = own_type
    elif fallback_type != "other":
        symbol_type = fallback_type
    else:
        symbol_type = own_type
    return name, symbol_type


def _split_large_node(
    node: Node,
    lines: list[str],
    rel_path: str,
    language: str,
    file_sha256: str,
    max_chunk_lines: int,
    file_mtime: float = 0.0,
    parent_symbol_name: str | None = None,
    parent_symbol_type: str | None = None,
) -> list[Chunk]:
    """Recursively split a node that exceeds max_chunk_lines."""
    fallback_name = (
        parent_symbol_name
        if parent_symbol_name is not None
        else _extract_symbol_name(node)
    )
    fallback_type = (
        parent_symbol_type
        if parent_symbol_type is not None
        else _classify_symbol_type(node.type)
    )

    start = node.start_point[0]
    end = node.end_point[0]
    total = end - start + 1

    if total <= max_chunk_lines:
        symbol_name, symbol_type = _chunk_symbol_metadata(node, fallback_name, fallback_type)
        content = "\n".join(lines[start:end + 1])
        return [Chunk(
            chunk_id=_make_chunk_id(rel_path, start + 1),
            content=content,
            rel_path=rel_path,
            language=language,
            start_line=start + 1,
            end_line=end + 1,
            symbol_name=symbol_name,
            symbol_type=symbol_type,
            file_sha256=file_sha256,
            file_mtime=file_mtime,
        )]

    # Try splitting at first-level children
    children = [c for c in node.children if c.type not in ("comment", "{", "}", "(", ")", ";")]
    if not children:
        # Can't split further — just use sliding window on this range
        return _sliding_window_range(
            lines, start, end, rel_path, language, file_sha256,
            max_chunk_lines, max_chunk_lines // 5, file_mtime=file_mtime,
            symbol_name=fallback_name,
            symbol_type=fallback_type,
        )

    chunks = []
    for child in children:
        chunks.extend(_split_large_node(
            child, lines, rel_path, language, file_sha256, max_chunk_lines,
            file_mtime=file_mtime,
            parent_symbol_name=fallback_name,
            parent_symbol_type=fallback_type,
        ))
    return chunks


def _sliding_window_range(
    lines: list[str],
    start: int,
    end: int,
    rel_path: str,
    language: str,
    file_sha256: str,
    max_lines: int,
    overlap: int,
    file_mtime: float = 0.0,
    symbol_name: str | None = None,
    symbol_type: str = "other",
) -> list[Chunk]:
    """Sliding window chunker for a range of lines."""
    chunks = []
    pos = start
    while pos <= end:
        chunk_end = min(pos + max_lines - 1, end)
        content = "\n".join(lines[pos:chunk_end + 1])
        chunks.append(Chunk(
            chunk_id=_make_chunk_id(rel_path, pos + 1),
            content=content,
            rel_path=rel_path,
            language=language,
            start_line=pos + 1,
            end_line=chunk_end + 1,
            symbol_name=symbol_name,
            symbol_type=symbol_type,
            file_sha256=file_sha256,
            file_mtime=file_mtime,
        ))
        if chunk_end >= end:
            break
        pos += max_lines - overlap
    return chunks


# Languages that tokenize heavily and need smaller chunks
_VERBOSE_LANGUAGES = {
    "xml", "json", "yaml", "markdown", "protobuf", "sql",
    "properties", "toml", "hcl", "dockerfile", "groovy",
}

_CONFIG_EXTENSIONS = frozenset({
    ".properties",
    ".ini", ".cfg", ".toml",
})
_CONFIG_FILENAMES = frozenset({
    "appsettings.json", "appsettings.development.json",
    "application.yml", "application.yaml", "application.properties",
    "config.json", "config.yaml",
    ".env", ".env.example", ".env.local",
})

_MANIFEST_FILENAMES = frozenset({
    "pom.xml", "package.json", "go.mod", "cargo.toml",
    "pyproject.toml", "build.gradle", "build.gradle.kts",
    "settings.gradle", "settings.gradle.kts",
    "requirements.txt", "setup.cfg",
})
_MANIFEST_EXTENSIONS = frozenset({
    ".csproj", ".fsproj", ".vbproj", ".nuspec",
})

_OPS_EXTENSIONS = frozenset({".tf", ".hcl"})
_OPS_FILENAMES = frozenset({
    "dockerfile", "jenkinsfile",
    "docker-compose.yml", "docker-compose.yaml",
})
_OPS_PATH_PATTERNS = (
    ".github/workflows/",
    ".gitlab-ci",
    "azure-pipelines",
    "build-pipeline/",
)
_OPS_PATH_PATTERNS_YAML_ONLY = ("templates/",)
# Path-based ops rules apply only to infra/config languages, not code (java, kotlin, etc.).
_OPS_PATH_LANGUAGES = frozenset({
    "yaml", "json", "xml", "bash", "powershell",
    "properties", "toml", "hcl", "dockerfile",
    "groovy", "markdown",
})


def _normalize_rel_path(rel_path: str) -> str:
    return rel_path.replace("\\", "/")


def _file_suffixes(rel_path: str) -> str:
    """Full compound suffix (e.g. .env.example) or last suffix."""
    p = PurePosixPath(_normalize_rel_path(rel_path))
    return "".join(p.suffixes).lower() or p.suffix.lower()


def _classify_file_symbol_type(rel_path: str, language: str) -> str | None:
    """Classify non-code files as config, manifest, or ops by path/name."""
    norm = _normalize_rel_path(rel_path).lower()
    name = PurePosixPath(norm).name
    ext = _file_suffixes(rel_path)
    last_ext = PurePosixPath(norm).suffix.lower()

    if (
        ext in _OPS_EXTENSIONS
        or last_ext in _OPS_EXTENSIONS
        or name in _OPS_FILENAMES
    ):
        return "ops"

    if (
        name in _MANIFEST_FILENAMES
        or ext in _MANIFEST_EXTENSIONS
        or last_ext in _MANIFEST_EXTENSIONS
    ):
        return "manifest"

    if (
        ext in _CONFIG_EXTENSIONS
        or last_ext in _CONFIG_EXTENSIONS
        or name in _CONFIG_FILENAMES
    ):
        return "config"

    if language in _OPS_PATH_LANGUAGES:
        for pattern in _OPS_PATH_PATTERNS:
            if pattern in norm:
                return "ops"
    if language == "yaml":
        for pattern in _OPS_PATH_PATTERNS_YAML_ONLY:
            if pattern in norm:
                return "ops"

    if language == "yaml":
        return "config"

    return None


def _apply_file_symbol_type(
    chunks: list[Chunk],
    rel_path: str,
    language: str,
) -> list[Chunk]:
    """Override symbol_type (and default symbol_name) for config/manifest/ops files."""
    file_type = _classify_file_symbol_type(rel_path, language)
    if not file_type:
        return chunks

    default_name = PurePosixPath(_normalize_rel_path(rel_path)).name
    for chunk in chunks:
        chunk.symbol_type = file_type
        if chunk.symbol_name is None:
            chunk.symbol_name = default_name
    return chunks

# Tree-sitter node types that represent import/using declarations per language.
# These lines are collected and selectively prepended to AST chunks when their
# symbols are referenced — primary signal for cross-project reference detection.
_IMPORT_NODE_TYPES: dict[str, frozenset[str]] = {
    "python": frozenset({"import_statement", "import_from_statement", "future_import_statement"}),
    "java": frozenset({"import_declaration", "package_declaration"}),
    "csharp": frozenset({"using_directive", "namespace_declaration"}),
    "javascript": frozenset({"import_statement", "import_declaration"}),
    "typescript": frozenset({"import_statement", "import_declaration"}),
    "go": frozenset({"import_declaration", "package_clause"}),
    "rust": frozenset({"use_declaration", "extern_crate_declaration", "mod_item"}),
    "c": frozenset({"preproc_include", "preproc_def"}),
    "cpp": frozenset({"preproc_include", "preproc_def", "using_declaration", "namespace_definition"}),
}

# Maximum number of lines to include in the prepended import header.
_MAX_IMPORT_HEADER_LINES = 35

# Sentinel: import line should always be prepended (package, wildcard, namespace).
_ALWAYS_INCLUDE_IMPORT = None


def _symbol_referenced_in_content(name: str, content: str) -> bool:
    """True if name appears as a whole identifier in content."""
    return (
        re.search(
            rf"(?<![A-Za-z0-9_]){re.escape(name)}(?![A-Za-z0-9_])",
            content,
        )
        is not None
    )


def _last_dotted_segment(qualified: str) -> str:
    return qualified.rsplit(".", 1)[-1]


def _parse_python_import_names(line: str) -> list[str] | None:
    stripped = line.strip()

    m = re.match(r"^import\s+([\w.]+)(?:\s+as\s+(\w+))?\s*$", stripped)
    if m:
        return [m.group(2) or _last_dotted_segment(m.group(1))]

    m = re.match(r"^from\s+[\w.]+\s+import\s+(.+)$", stripped)
    if not m:
        return []

    tail = m.group(1).strip()
    if tail == "*":
        return _ALWAYS_INCLUDE_IMPORT

    names: list[str] = []
    for part in re.split(r"\s*,\s*", tail):
        part = part.strip()
        if not part:
            continue
        alias_m = re.match(r"^([\w.]+)\s+as\s+(\w+)$", part)
        if alias_m:
            names.append(alias_m.group(2))
        else:
            names.append(_last_dotted_segment(part))
    return names


def _parse_java_import_names(line: str) -> list[str] | None:
    stripped = line.strip()
    if stripped.startswith("package "):
        return _ALWAYS_INCLUDE_IMPORT

    m = re.match(r"^import\s+(?:static\s+)?(.+?)\s*;\s*$", stripped)
    if not m:
        return []

    qualified = m.group(1).strip()
    if qualified.endswith(".*"):
        return _ALWAYS_INCLUDE_IMPORT
    return [_last_dotted_segment(qualified)]


def _parse_csharp_using_names(line: str) -> list[str] | None:
    stripped = line.strip()
    if stripped.startswith("namespace ") and "{" not in stripped:
        return _ALWAYS_INCLUDE_IMPORT

    m = re.match(r"^using\s+(?:static\s+)?(.+?)\s*;\s*$", stripped)
    if not m:
        return []

    qualified = m.group(1).strip()
    if qualified.endswith("*"):
        return _ALWAYS_INCLUDE_IMPORT
    return [_last_dotted_segment(qualified)]


def _parse_js_import_names(line: str) -> list[str]:
    stripped = line.strip()
    names: list[str] = []

    default_m = re.match(r"^import\s+(\w+)[\s,]", stripped)
    if default_m:
        names.append(default_m.group(1))

    brace_m = re.search(r"\{([^}]+)\}", stripped)
    if brace_m:
        for part in re.split(r"\s*,\s*", brace_m.group(1)):
            part = part.strip()
            if not part:
                continue
            alias_m = re.match(r"^(\w+)\s+as\s+(\w+)$", part)
            names.append(alias_m.group(2) if alias_m else part.split()[0])
        return names

    ns_m = re.match(r"^import\s+\*\s+as\s+(\w+)", stripped)
    if ns_m:
        return [ns_m.group(1)]

    return names if names else []


def _go_import_path_name(import_path: str) -> str:
    """Last path segment of a Go import path (e.g. net/http -> http)."""
    path = import_path.rstrip("/")
    return path.rsplit("/", 1)[-1]


def _parse_go_import_names(line: str) -> list[str] | None:
    stripped = line.strip()
    if stripped.startswith("package "):
        return _ALWAYS_INCLUDE_IMPORT

    m = re.match(r'^import\s+"([^"]+)"\s*$', stripped)
    if m:
        return [_go_import_path_name(m.group(1))]

    m = re.match(r'^(?:\w+\s+)?"([^"]+)"\s*$', stripped)
    if m:
        return [_go_import_path_name(m.group(1))]

    return []


def _parse_rust_use_names(line: str) -> list[str] | None:
    stripped = line.strip()
    m = re.match(r"^use\s+(.+);\s*$", stripped)
    if not m:
        return []

    path = m.group(1).strip()
    if path.endswith("::*") or path == "*":
        return _ALWAYS_INCLUDE_IMPORT

    alias_m = re.search(r"\s+as\s+(\w+)\s*$", path)
    if alias_m:
        return [alias_m.group(1)]

    brace_m = re.search(r"\{([^}]+)\}", path)
    if brace_m:
        return [name.strip() for name in brace_m.group(1).split(",") if name.strip()]

    if "::" in path:
        return [_last_dotted_segment(path.replace("::", "."))]
    return [_last_dotted_segment(path)]


def _parse_c_cpp_include_names(line: str) -> list[str]:
    stripped = line.strip()
    m = re.match(r'#include\s+"([^"]+)"', stripped)
    if m:
        base = m.group(1).rsplit("/", 1)[-1]
        if base.endswith(".h"):
            base = base[:-2]
        return [base]
    m = re.match(r"#define\s+(\w+)", stripped)
    if m:
        return [m.group(1)]
    return []


_IMPORT_NAME_PARSERS: dict[str, object] = {
    "python": _parse_python_import_names,
    "java": _parse_java_import_names,
    "csharp": _parse_csharp_using_names,
    "javascript": _parse_js_import_names,
    "typescript": _parse_js_import_names,
    "go": _parse_go_import_names,
    "rust": _parse_rust_use_names,
    "c": _parse_c_cpp_include_names,
    "cpp": _parse_c_cpp_include_names,
}


def _extract_imported_names(line: str, language: str) -> list[str] | None:
    """Names from an import line to match against chunk content.

    Returns None if the line should always be prepended (package, wildcard).
    Returns an empty list when no names could be parsed.
    """
    parser = _IMPORT_NAME_PARSERS.get(language)
    if parser is None:
        return []

    result = parser(line)  # type: ignore[operator]
    if result is _ALWAYS_INCLUDE_IMPORT:
        return None
    return result


def _filter_relevant_imports(
    import_lines: list[str],
    chunk_content: str,
    language: str,
) -> list[str]:
    """Return import lines whose symbols are referenced in chunk_content."""
    relevant: list[str] = []
    for line in import_lines:
        names = _extract_imported_names(line, language)
        if names is None:
            relevant.append(line)
            continue
        if not names:
            continue
        if any(_symbol_referenced_in_content(name, chunk_content) for name in names):
            relevant.append(line)
    return relevant


def _collect_import_lines(tree_root: "Node", lines: list[str], language: str) -> list[str]:
    """Collect import/using declaration lines from the AST root.

    Scans top-level children for import-like node types. Capped at
    _MAX_IMPORT_HEADER_LINES. Returns an empty list if none are found.
    """
    import_types = _IMPORT_NODE_TYPES.get(language)
    if not import_types:
        return []

    import_lines: list[str] = []
    for child in tree_root.children:
        if child.type in import_types:
            start = child.start_point[0]
            end = child.end_point[0]
            import_lines.extend(lines[start : end + 1])

    if not import_lines:
        return []

    if len(import_lines) > _MAX_IMPORT_HEADER_LINES:
        import_lines = import_lines[:_MAX_IMPORT_HEADER_LINES]

    return import_lines


def _prepend_import_header(chunk: Chunk, header: str) -> None:
    """Prepend header to chunk content unless it is already present."""
    preview = header[:20] if len(header) >= 20 else header
    if preview and chunk.content.startswith(preview):
        return
    chunk.content = header + "\n" + chunk.content


def chunk_file(
    content: str,
    rel_path: str,
    language: str,
    file_sha256: str,
    max_chunk_lines: int = 150,
    chunk_overlap_lines: int = 20,
    file_mtime: float = 0.0,
) -> list[Chunk]:
    """Chunk a file using Tree-sitter AST or sliding window fallback."""
    lines = content.splitlines()
    if not lines:
        return []

    # Use smaller chunks for verbose/markup languages to stay within token limits
    if language in _VERBOSE_LANGUAGES:
        max_chunk_lines = min(max_chunk_lines, 60)
        chunk_overlap_lines = min(chunk_overlap_lines, 10)

    lang_obj = LANGUAGES.get(language)
    node_types = EXTRACT_NODE_TYPES.get(language)

    if lang_obj is None or node_types is None:
        if language not in SLIDING_WINDOW_LANGUAGES:
            log.info("unsupported_language_fallback", language=language, path=rel_path)
        chunks = _sliding_window_range(
            lines, 0, len(lines) - 1, rel_path, language, file_sha256,
            max_chunk_lines, chunk_overlap_lines, file_mtime=file_mtime,
        )
        return _apply_file_symbol_type(chunks, rel_path, language)

    try:
        parser = _PARSERS.get(language) or Parser(lang_obj)
        tree = parser.parse(content.encode("utf-8"))
    except Exception as e:
        log.warning("treesitter_parse_failure", path=rel_path, error=str(e))
        chunks = _sliding_window_range(
            lines, 0, len(lines) - 1, rel_path, language, file_sha256,
            max_chunk_lines, chunk_overlap_lines, file_mtime=file_mtime,
        )
        return _apply_file_symbol_type(chunks, rel_path, language)

    chunks: list[Chunk] = []

    def _extract_from_node(node: Node) -> None:
        if node.type in node_types:
            start = node.start_point[0]
            end = node.end_point[0]
            total = end - start + 1

            if total > max_chunk_lines:
                extracted = _split_large_node(
                    node, lines, rel_path, language, file_sha256, max_chunk_lines,
                    file_mtime=file_mtime,
                    parent_symbol_name=_extract_symbol_name(node),
                    parent_symbol_type=_classify_symbol_type(node.type),
                )
            else:
                node_content = "\n".join(lines[start:end + 1])
                extracted = [Chunk(
                    chunk_id=_make_chunk_id(rel_path, start + 1),
                    content=node_content,
                    rel_path=rel_path,
                    language=language,
                    start_line=start + 1,
                    end_line=end + 1,
                    symbol_name=_extract_symbol_name(node),
                    symbol_type=_classify_symbol_type(node.type),
                    file_sha256=file_sha256,
                    file_mtime=file_mtime,
                )]

            chunks.extend(extracted)
        else:
            for child in node.children:
                _extract_from_node(child)

    _extract_from_node(tree.root_node)

    if not chunks:
        chunks = _sliding_window_range(
            lines, 0, len(lines) - 1, rel_path, language, file_sha256,
            max_chunk_lines, chunk_overlap_lines, file_mtime=file_mtime,
        )
    else:
        import_lines = _collect_import_lines(tree.root_node, lines, language)
        if import_lines:
            for chunk in chunks:
                relevant = _filter_relevant_imports(import_lines, chunk.content, language)
                if relevant:
                    _prepend_import_header(chunk, "\n".join(relevant))

    return _apply_file_symbol_type(chunks, rel_path, language)
