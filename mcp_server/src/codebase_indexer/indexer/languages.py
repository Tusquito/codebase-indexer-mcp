# src/codebase_indexer/indexer/languages.py
"""Single source of truth for language support.

Adding a language is a one-line edit here: declare its file extensions and,
for AST-aware chunking, the tree-sitter grammar plus the node types to extract.
Languages without a grammar loader fall back to sliding-window chunking.

Grammar loaders import their tree-sitter module lazily so this registry can be
imported (e.g. by the file scanner, which only needs the extension map) without
pulling in every tree-sitter grammar.
"""

from collections.abc import Callable
from dataclasses import dataclass, field


@dataclass(frozen=True)
class LanguageSpec:
    name: str
    extensions: tuple[str, ...]
    node_types: frozenset[str] = field(default_factory=frozenset)
    # Returns a tree_sitter.Language when AST chunking is supported, else None.
    grammar_loader: Callable[[], object] | None = None


def _loader(module_name: str, fn_name: str = "language") -> Callable[[], object]:
    """Build a lazy tree-sitter grammar loader for the given module."""

    def load() -> object:
        import importlib

        from tree_sitter import Language

        module = importlib.import_module(module_name)
        return Language(getattr(module, fn_name)())

    return load


# Order is irrelevant; extensions must be unique across specs.
LANGUAGE_SPECS: list[LanguageSpec] = [
    # --- AST-aware (tree-sitter) languages ---
    LanguageSpec(
        "python", (".py",),
        frozenset({"function_definition", "class_definition", "decorated_definition"}),
        _loader("tree_sitter_python"),
    ),
    LanguageSpec(
        "javascript", (".js", ".jsx"),
        frozenset({
            "function_declaration", "class_declaration", "arrow_function",
            "method_definition", "export_statement",
        }),
        _loader("tree_sitter_javascript"),
    ),
    LanguageSpec(
        "typescript", (".ts", ".tsx"),
        frozenset({
            "function_declaration", "class_declaration", "arrow_function",
            "method_definition", "export_statement",
        }),
        _loader("tree_sitter_typescript", "language_typescript"),
    ),
    LanguageSpec(
        "go", (".go",),
        frozenset({"function_declaration", "method_declaration", "type_declaration"}),
        _loader("tree_sitter_go"),
    ),
    LanguageSpec(
        "rust", (".rs",),
        frozenset({"function_item", "impl_item", "struct_item", "enum_item", "trait_item"}),
        _loader("tree_sitter_rust"),
    ),
    LanguageSpec(
        "java", (".java",),
        frozenset({"class_declaration", "method_declaration", "interface_declaration"}),
        _loader("tree_sitter_java"),
    ),
    LanguageSpec(
        "c", (".c", ".h"),
        frozenset({"function_definition", "class_specifier", "struct_specifier"}),
        _loader("tree_sitter_c"),
    ),
    LanguageSpec(
        "cpp", (".cpp", ".cc", ".cxx", ".hpp"),
        frozenset({"function_definition", "class_specifier", "struct_specifier"}),
        _loader("tree_sitter_cpp"),
    ),
    LanguageSpec(
        "csharp", (".cs",),
        frozenset({
            "class_declaration", "method_declaration", "interface_declaration",
            "struct_declaration", "enum_declaration", "namespace_declaration",
            "constructor_declaration",
        }),
        _loader("tree_sitter_c_sharp"),
    ),
    # --- Markup / data / scripting (sliding-window chunking) ---
    LanguageSpec("xml", (".xml", ".xsd", ".xsl", ".xslt", ".wsdl")),
    LanguageSpec("yaml", (".yml", ".yaml")),
    LanguageSpec("json", (".json",)),
    LanguageSpec("protobuf", (".proto",)),
    LanguageSpec("sql", (".sql",)),
    LanguageSpec("kotlin", (".kt", ".kts")),
    LanguageSpec("scala", (".scala",)),
    LanguageSpec("ruby", (".rb",)),
    LanguageSpec("php", (".php",)),
    LanguageSpec("swift", (".swift",)),
    LanguageSpec("dart", (".dart",)),
    LanguageSpec("bash", (".sh", ".bash")),
    LanguageSpec("powershell", (".ps1",)),
    LanguageSpec("markdown", (".md",)),
]

# {".py": "python", ...} — cheap to build, no grammar imports triggered.
EXTENSION_LANGUAGE_MAP: dict[str, str] = {
    ext: spec.name for spec in LANGUAGE_SPECS for ext in spec.extensions
}

# Specs that support AST-based chunking (have a tree-sitter grammar loader).
AST_LANGUAGE_SPECS: list[LanguageSpec] = [
    spec for spec in LANGUAGE_SPECS if spec.grammar_loader is not None
]
