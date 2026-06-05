# src/codebase_indexer/tools/build_deps.py
"""Build dependency extraction across common package ecosystems.

Parses build manifest files (pom.xml, *.csproj, package.json, go.mod, …) to
extract inter-project dependencies.  Used by map_service_dependencies and
find_cross_references to surface build-level relationships alongside HTTP ones.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import PurePosixPath

# ---------------------------------------------------------------------------
# Data model
# ---------------------------------------------------------------------------

@dataclass
class BuildDep:
    """One declared dependency extracted from a build manifest file."""

    artifact: str          # artifact / package / module name
    group: str = ""        # group / namespace (Maven groupId, Go module prefix…)
    version: str = ""
    scope: str = ""        # e.g. "compile", "test", "devDependency"
    ecosystem: str = ""    # "maven" | "nuget" | "npm" | "gradle" | "go" | "cargo" | "python"


# ---------------------------------------------------------------------------
# Build manifest detection
# ---------------------------------------------------------------------------

_MANIFEST_FILENAMES = frozenset({
    "pom.xml",
    "build.gradle", "build.gradle.kts",
    "settings.gradle", "settings.gradle.kts",
    "package.json",
    "go.mod",
    "Cargo.toml",
    "pyproject.toml",
    "requirements.txt",
    "setup.cfg",
})

_MANIFEST_SUFFIXES = (".csproj", ".fsproj", ".vbproj", ".nuspec")


def is_build_manifest(rel_path: str) -> bool:
    """Return True if rel_path is a known build manifest file."""
    p = PurePosixPath(rel_path.replace("\\", "/"))
    if p.name in _MANIFEST_FILENAMES:
        return True
    return any(rel_path.endswith(suf) for suf in _MANIFEST_SUFFIXES)


# ---------------------------------------------------------------------------
# Per-ecosystem extractors
# ---------------------------------------------------------------------------

# -- Maven (pom.xml) ---------------------------------------------------------

_MVN_ARTIFACT = re.compile(
    r"<groupId>([^<]{1,200})</groupId>\s*<artifactId>([^<]{1,200})</artifactId>"
    r"(?:\s*<version>([^<]{1,100})</version>)?",
    re.DOTALL,
)
_MVN_SCOPE = re.compile(r"<scope>([^<]{1,50})</scope>")


def _extract_maven(content: str) -> list[BuildDep]:
    """Parse Maven pom.xml dependency and parent blocks."""
    deps: list[BuildDep] = []
    # Find each <dependency> block and parse group/artifact/version/scope
    for block_m in re.finditer(r"<dependency>(.*?)</dependency>", content, re.DOTALL):
        block = block_m.group(1)
        m = _MVN_ARTIFACT.search(block)
        if not m:
            continue
        group = m.group(1).strip()
        artifact = m.group(2).strip()
        version = (m.group(3) or "").strip()
        scope_m = _MVN_SCOPE.search(block)
        scope = scope_m.group(1).strip() if scope_m else "compile"
        deps.append(BuildDep(
            artifact=artifact, group=group, version=version,
            scope=scope, ecosystem="maven",
        ))
    # Also capture parent pom (structural, not inside <dependency>)
    parent_m = re.search(
        r"<parent>.*?<groupId>([^<]{1,200})</groupId>\s*<artifactId>([^<]{1,200})</artifactId>"
        r"(?:.*?<version>([^<]{1,100})</version>)?",
        content, re.DOTALL,
    )
    if parent_m:
        deps.append(BuildDep(
            artifact=parent_m.group(2).strip(),
            group=parent_m.group(1).strip(),
            version=(parent_m.group(3) or "").strip(),
            scope="parent",
            ecosystem="maven",
        ))
    return deps


# -- NuGet (.csproj / .fsproj / .vbproj) ------------------------------------

_NUGET_PKG_REF = re.compile(
    r'<PackageReference\s+Include\s*=\s*["\']([^"\']{1,300})["\']'
    r'(?:[^>]*?Version\s*=\s*["\']([^"\']{1,100})["\'])?',
    re.IGNORECASE,
)
_NUGET_PROJ_REF = re.compile(
    r'<ProjectReference\s+Include\s*=\s*["\']([^"\']{1,300})["\']',
    re.IGNORECASE,
)


def _extract_nuget(content: str) -> list[BuildDep]:
    """Parse NuGet PackageReference and ProjectReference from .csproj/.fsproj."""
    deps: list[BuildDep] = []
    for m in _NUGET_PKG_REF.finditer(content):
        name = m.group(1).strip()
        version = (m.group(2) or "").strip()
        deps.append(BuildDep(artifact=name, version=version, ecosystem="nuget"))
    for m in _NUGET_PROJ_REF.finditer(content):
        # ProjectReference paths like ..\MyProject\MyProject.csproj → extract name
        path = m.group(1).strip().replace("\\", "/")
        artifact = PurePosixPath(path).stem
        deps.append(BuildDep(artifact=artifact, scope="project", ecosystem="nuget"))
    return deps


# -- npm (package.json) ------------------------------------------------------

_NPM_DEP_SECTION = re.compile(
    r'"(dependencies|devDependencies|peerDependencies)"\s*:\s*\{([^}]{1,4000})\}',
    re.DOTALL,
)
_NPM_ENTRY = re.compile(r'"([^"]{1,200})"\s*:\s*"([^"]{1,100})"')


def _extract_npm(content: str) -> list[BuildDep]:
    """Parse npm dependencies/devDependencies from package.json."""
    deps: list[BuildDep] = []
    for sec_m in _NPM_DEP_SECTION.finditer(content):
        scope = sec_m.group(1)  # "dependencies" | "devDependencies" | ...
        block = sec_m.group(2)
        for m in _NPM_ENTRY.finditer(block):
            deps.append(BuildDep(
                artifact=m.group(1).strip(),
                version=m.group(2).strip(),
                scope=scope,
                ecosystem="npm",
            ))
    return deps


# -- Gradle (build.gradle / build.gradle.kts) --------------------------------

# Matches: implementation 'group:artifact:version'  or  implementation("g:a:v")
_GRADLE_DEP = re.compile(
    r'(?:implementation|api|compileOnly|runtimeOnly|testImplementation|testRuntimeOnly|annotationProcessor|kapt|compile)\s*'
    r'[(\'"]([\w.\-]+):([\w.\-]+)(?::([\w.\-+]+))?[)\'"]',
    re.IGNORECASE,
)
# Kotlin DSL: implementation(project(":sub-module"))
_GRADLE_PROJ = re.compile(r'project\s*\(\s*["\']:([^"\')\s]{1,200})["\']', re.IGNORECASE)


def _extract_gradle(content: str) -> list[BuildDep]:
    """Parse Gradle/Maven-coordinate and project() dependencies."""
    deps: list[BuildDep] = []
    for m in _GRADLE_DEP.finditer(content):
        deps.append(BuildDep(
            group=m.group(1).strip(),
            artifact=m.group(2).strip(),
            version=(m.group(3) or "").strip(),
            ecosystem="gradle",
        ))
    for m in _GRADLE_PROJ.finditer(content):
        artifact = m.group(1).strip().lstrip(":")
        deps.append(BuildDep(artifact=artifact, scope="project", ecosystem="gradle"))
    return deps


# -- Go (go.mod) -------------------------------------------------------------

_GO_REQUIRE_BLOCK = re.compile(r"require\s*\(([^)]{1,5000})\)", re.DOTALL)
_GO_REQUIRE_SINGLE = re.compile(r"^\s*require\s+(\S+)\s+(\S+)", re.MULTILINE)
_GO_MODULE_LINE = re.compile(r"^\s*(\S+)\s+(\S+)", re.MULTILINE)


def _extract_go(content: str) -> list[BuildDep]:
    """Parse Go module require blocks from go.mod."""
    deps: list[BuildDep] = []
    seen: set[str] = set()

    def _add(module: str, version: str) -> None:
        if module and module not in seen:
            seen.add(module)
            # module path like github.com/org/pkg → artifact = last segment
            artifact = module.split("/")[-1]
            deps.append(BuildDep(
                artifact=artifact, group=module, version=version, ecosystem="go",
            ))

    for block_m in _GO_REQUIRE_BLOCK.finditer(content):
        for m in _GO_MODULE_LINE.finditer(block_m.group(1)):
            mod = m.group(1).strip()
            ver = m.group(2).strip()
            if mod.startswith("//") or mod.startswith("/*"):
                continue
            _add(mod, ver)

    for m in _GO_REQUIRE_SINGLE.finditer(content):
        _add(m.group(1).strip(), m.group(2).strip())

    return deps


# -- Cargo (Cargo.toml) ------------------------------------------------------

_CARGO_DEP_SECTION = re.compile(r"\[dependencies\](.*?)(?=\n\s*\[|\Z)", re.DOTALL)
_CARGO_DEV_SECTION = re.compile(r"\[dev-dependencies\](.*?)(?=\n\s*\[|\Z)", re.DOTALL)
_CARGO_ENTRY = re.compile(r'^\s*(\w[\w-]{0,100})\s*=\s*["\']?([^{\n\r\[]{0,100}?)["\']?\s*$', re.MULTILINE)


def _extract_cargo(content: str) -> list[BuildDep]:
    """Parse Cargo.toml [dependencies] and [dev-dependencies] sections."""
    deps: list[BuildDep] = []

    def _parse_section(section_content: str, scope: str) -> None:
        for m in _CARGO_ENTRY.finditer(section_content):
            name = m.group(1).strip()
            version = m.group(2).strip().strip('"\'{}')
            deps.append(BuildDep(artifact=name, version=version, scope=scope, ecosystem="cargo"))

    for m in _CARGO_DEP_SECTION.finditer(content):
        _parse_section(m.group(1), "")
    for m in _CARGO_DEV_SECTION.finditer(content):
        _parse_section(m.group(1), "dev")

    return deps


# -- Python (pyproject.toml / requirements.txt / setup.cfg) -----------------

_PYPROJECT_DEPS = re.compile(r'\[project\].*?dependencies\s*=\s*\[(.*?)\]', re.DOTALL)
_REQ_LINE = re.compile(r'^\s*([A-Za-z0-9_\-\.]{1,200})\s*(?:[>=<!~^]{1,2}\s*[\S]+)?', re.MULTILINE)


def _extract_python(content: str, filename: str) -> list[BuildDep]:
    """Parse Python deps from pyproject.toml, requirements.txt, or setup.cfg."""
    deps: list[BuildDep] = []

    if filename == "pyproject.toml":
        for sec_m in _PYPROJECT_DEPS.finditer(content):
            for line in sec_m.group(1).splitlines():
                line = line.strip().strip('",')
                if line and not line.startswith("#"):
                    m = _REQ_LINE.match(line)
                    if m:
                        deps.append(BuildDep(artifact=m.group(1).strip(), ecosystem="python"))
    else:
        # requirements.txt / setup.cfg
        for m in _REQ_LINE.finditer(content):
            name = m.group(1).strip()
            if name and not name.startswith("#") and not name.startswith("-"):
                deps.append(BuildDep(artifact=name, ecosystem="python"))

    return deps


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------

def extract_build_deps(content: str, rel_path: str) -> list[BuildDep]:
    """Extract build dependencies from a manifest file's content.

    Routes to the ecosystem-appropriate parser based on the filename.
    Returns an empty list for non-manifest files (check is_build_manifest first).
    """
    filename = PurePosixPath(rel_path.replace("\\", "/")).name
    suffix = PurePosixPath(rel_path.replace("\\", "/")).suffix.lower()

    if filename == "pom.xml":
        return _extract_maven(content)
    if suffix in (".csproj", ".fsproj", ".vbproj", ".nuspec"):
        return _extract_nuget(content)
    if filename == "package.json":
        return _extract_npm(content)
    if filename in ("build.gradle", "build.gradle.kts",
                    "settings.gradle", "settings.gradle.kts"):
        return _extract_gradle(content)
    if filename == "go.mod":
        return _extract_go(content)
    if filename == "Cargo.toml":
        return _extract_cargo(content)
    if filename in ("pyproject.toml", "requirements.txt", "setup.cfg"):
        return _extract_python(content, filename)

    return []


# ---------------------------------------------------------------------------
# Collection-name matching
# ---------------------------------------------------------------------------

# Suffixes stripped before comparing artifact → collection name.
# Handles patterns like artifact "myapp-contracts-definitions" → collection "myapp-contracts".
_STRIP_SUFFIXES = (
    "-definitions", "-definition",
    "-api", "-apis",
    "-client", "-clients",
    "-core", "-commons", "-common",
    "-lib", "-library",
    "-impl", "-implementation",
    "-service", "-services",
    "-server",
    "-starter",
)


def _normalize_name(name: str) -> str:
    """Lowercase + strip common architectural suffixes for fuzzy matching."""
    n = name.lower().replace("_", "-").replace(".", "-")
    for suf in _STRIP_SUFFIXES:
        if n.endswith(suf):
            n = n[: -len(suf)]
            break
    return n


def match_deps_to_collections(
    deps: list[BuildDep],
    collection_names: list[str],
    self_collection: str = "",
) -> list[dict]:
    """Match extracted build deps against a list of indexed collection names.

    Returns a list of match dicts:
        { "artifact": str, "group": str, "version": str, "scope": str,
          "ecosystem": str, "matched_collection": str, "match_confidence": "exact"|"fuzzy" }

    Strategy:
    - exact: artifact name == collection name (case-insensitive)
    - fuzzy: normalized artifact contains or is contained in normalized collection name
    """
    candidates = [c for c in collection_names if c != self_collection]
    norm_candidates = {c: _normalize_name(c) for c in candidates}

    matches: list[dict] = []
    seen: set[str] = set()

    for dep in deps:
        norm_artifact = _normalize_name(dep.artifact)
        norm_group = _normalize_name(dep.group) if dep.group else ""

        for coll, norm_coll in norm_candidates.items():
            # Check artifact name or group against collection name
            matched = False
            confidence = "fuzzy"

            if norm_artifact == norm_coll or norm_group == norm_coll:
                matched = True
                confidence = "exact"
            elif len(norm_artifact) >= 4 and (
                norm_artifact in norm_coll or norm_coll in norm_artifact
            ):
                matched = True
            elif norm_group and len(norm_group) >= 4 and (
                norm_group in norm_coll or norm_coll in norm_group
            ):
                matched = True

            if matched:
                key = f"{dep.artifact}:{coll}"
                if key not in seen:
                    seen.add(key)
                    matches.append({
                        "artifact": dep.artifact,
                        "group": dep.group,
                        "version": dep.version,
                        "scope": dep.scope,
                        "ecosystem": dep.ecosystem,
                        "matched_collection": coll,
                        "match_confidence": confidence,
                    })

    return matches
