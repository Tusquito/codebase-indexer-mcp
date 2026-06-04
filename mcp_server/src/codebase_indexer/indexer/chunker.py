# src/codebase_indexer/indexer/chunker.py
"""AST-based code chunking using Tree-sitter, with sliding window fallback."""

import hashlib
from dataclasses import dataclass

import structlog
from tree_sitter import Language, Parser, Node

from codebase_indexer.indexer.languages import AST_LANGUAGE_SPECS

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
        if child.type in ("identifier", "name", "property_identifier", "type_identifier"):
            return child.text.decode("utf-8", errors="replace") if child.text is not None else None
        # For decorated_definition / export_statement, look deeper
        if child.type in ("function_definition", "class_definition", "function_declaration",
                          "class_declaration", "method_declaration"):
            return _extract_symbol_name(child)
    return None


def _classify_symbol_type(node_type: str) -> str:
    """Classify the AST node type into a semantic category."""
    if "class" in node_type or "struct" in node_type or "enum" in node_type or "interface" in node_type:
        return "class"
    if "method" in node_type or "constructor" in node_type:
        return "method"
    if "function" in node_type or "arrow_function" in node_type:
        return "function"
    return "other"


def _split_large_node(
    node: Node,
    lines: list[str],
    rel_path: str,
    language: str,
    file_sha256: str,
    max_chunk_lines: int,
    file_mtime: float = 0.0,
) -> list[Chunk]:
    """Recursively split a node that exceeds max_chunk_lines."""
    start = node.start_point[0]
    end = node.end_point[0]
    total = end - start + 1

    if total <= max_chunk_lines:
        content = "\n".join(lines[start:end + 1])
        return [Chunk(
            chunk_id=_make_chunk_id(rel_path, start + 1),
            content=content,
            rel_path=rel_path,
            language=language,
            start_line=start + 1,
            end_line=end + 1,
            symbol_name=_extract_symbol_name(node),
            symbol_type=_classify_symbol_type(node.type),
            file_sha256=file_sha256,
            file_mtime=file_mtime,
        )]

    # Try splitting at first-level children
    children = [c for c in node.children if c.type not in ("comment", "{", "}", "(", ")", ";")]
    if not children:
        # Can't split further — just use sliding window on this range
        return _sliding_window_range(lines, start, end, rel_path, language, file_sha256, max_chunk_lines, max_chunk_lines // 5, file_mtime=file_mtime)

    chunks = []
    for child in children:
        chunks.extend(_split_large_node(child, lines, rel_path, language, file_sha256, max_chunk_lines, file_mtime=file_mtime))
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
            symbol_name=None,
            symbol_type="other",
            file_sha256=file_sha256,
            file_mtime=file_mtime,
        ))
        if chunk_end >= end:
            break
        pos += max_lines - overlap
    return chunks


# Languages that tokenize heavily and need smaller chunks
_VERBOSE_LANGUAGES = {"xml", "json", "yaml", "markdown", "protobuf", "sql"}

# Tree-sitter node types that represent import/using declarations per language.
# These lines are collected as an "import header" and prepended to every AST
# chunk so that semantic search can match on imported types and packages —
# which is the primary signal for cross-project reference detection.
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


def _collect_import_header(tree_root: "Node", lines: list[str], language: str) -> str:
    """Collect import/using declarations from the AST root as a header string.

    Scans the top-level children of the AST root for import-like node types and
    returns their source lines joined as a single string (capped at
    _MAX_IMPORT_HEADER_LINES lines).  Returns an empty string if the language
    has no import node types configured or none are found.
    """
    import_types = _IMPORT_NODE_TYPES.get(language)
    if not import_types:
        return ""

    import_lines: list[str] = []
    for child in tree_root.children:
        if child.type in import_types:
            start = child.start_point[0]
            end = child.end_point[0]
            import_lines.extend(lines[start : end + 1])

    if not import_lines:
        return ""

    # Cap to avoid bloating chunks in files with hundreds of imports
    if len(import_lines) > _MAX_IMPORT_HEADER_LINES:
        import_lines = import_lines[:_MAX_IMPORT_HEADER_LINES]

    return "\n".join(import_lines)


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
        log.info("unsupported_language_fallback", language=language, path=rel_path)
        return _sliding_window_range(
            lines, 0, len(lines) - 1, rel_path, language, file_sha256,
            max_chunk_lines, chunk_overlap_lines, file_mtime=file_mtime,
        )

    try:
        parser = _PARSERS.get(language) or Parser(lang_obj)
        tree = parser.parse(content.encode("utf-8"))
    except Exception as e:
        log.warning("treesitter_parse_failure", path=rel_path, error=str(e))
        return _sliding_window_range(
            lines, 0, len(lines) - 1, rel_path, language, file_sha256,
            max_chunk_lines, chunk_overlap_lines, file_mtime=file_mtime,
        )

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

    # If no AST nodes found, use sliding window on entire file
    if not chunks:
        return _sliding_window_range(
            lines, 0, len(lines) - 1, rel_path, language, file_sha256,
            max_chunk_lines, chunk_overlap_lines, file_mtime=file_mtime,
        )

    # Prepend import/using declarations to each chunk so that semantic search
    # can match on imported types and packages — the primary signal for
    # cross-project reference detection (e.g. a Java class that imports
    # com.udh.interface.Foo links back to the udh-interface collection).
    import_header = _collect_import_header(tree.root_node, lines, language)
    if import_header:
        for chunk in chunks:
            # Only prepend if the chunk doesn't already start with imports
            # (avoids doubling up on chunks that ARE the import block).
            if not chunk.content.startswith(import_header[:20]):
                chunk.content = import_header + "\n" + chunk.content

    return chunks
