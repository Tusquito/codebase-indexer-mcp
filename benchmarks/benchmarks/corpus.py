"""Deterministic synthetic-corpus generator for benchmarks.

Generates a configurable, reproducible repository of mixed-language source
files into a directory. The same (seed, n_files) always produces the same
tree, so benchmark runs are comparable across machines and over time without
depending on any private repository.

The generated code is intentionally shaped so the tree-sitter chunker extracts
realistic symbols (functions, classes, methods) rather than collapsing every
file into a single sliding-window chunk.
"""

from __future__ import annotations

import random
from dataclasses import dataclass
from pathlib import Path

# Weighted language mix (extension, weight). Mirrors a typical polyglot repo.
_LANGUAGE_WEIGHTS: list[tuple[str, int]] = [
    ("py", 30),
    ("ts", 25),
    ("go", 15),
    ("java", 15),
    ("yaml", 15),
]


@dataclass
class CorpusStats:
    root: str
    project_name: str
    n_files: int
    files_by_ext: dict[str, int]
    total_bytes: int


def _py_file(rng: random.Random, idx: int) -> str:
    n_funcs = rng.randint(2, 6)
    parts = ['"""Module {}."""'.format(idx), "import os", "import sys", ""]
    for f in range(n_funcs):
        body = "\n".join(
            f"    x{j} = {rng.randint(0, 99)} + len(os.getcwd())" for j in range(rng.randint(1, 5))
        )
        parts.append(f"def func_{idx}_{f}(a, b, c=None):")
        parts.append(body)
        parts.append("    return x0 if a else b")
        parts.append("")
    parts.append(f"class Service_{idx}:")
    for m in range(rng.randint(2, 5)):
        parts.append(f"    def method_{m}(self, value):")
        parts.append(f"        return value * {rng.randint(2, 9)}")
        parts.append("")
    return "\n".join(parts) + "\n"


def _ts_file(rng: random.Random, idx: int) -> str:
    parts = [f"// component {idx}", "import {EventEmitter} from 'events';", ""]
    for f in range(rng.randint(2, 5)):
        parts.append(f"export function handle_{idx}_{f}(req: Request): Response {{")
        parts.append(f"  const n = {rng.randint(1, 50)};")
        parts.append("  return process(req, n);")
        parts.append("}")
        parts.append("")
    parts.append(f"export class Client_{idx} {{")
    for m in range(rng.randint(2, 4)):
        parts.append(f"  async call_{m}(path: string) {{")
        parts.append(f'    return fetch("/api/service{idx}/item{m}");')
        parts.append("  }")
    parts.append("}")
    return "\n".join(parts) + "\n"


def _go_file(rng: random.Random, idx: int) -> str:
    parts = [f"package svc{idx % 5}", "", 'import "fmt"', ""]
    parts.append(f"type Model_{idx} struct {{")
    parts.append("  ID   int")
    parts.append("  Name string")
    parts.append("}")
    parts.append("")
    for f in range(rng.randint(2, 5)):
        parts.append(f"func Handler_{idx}_{f}(id int) (int, error) {{")
        parts.append(f"  fmt.Println({rng.randint(1, 100)})")
        parts.append("  return id, nil")
        parts.append("}")
        parts.append("")
    return "\n".join(parts) + "\n"


def _java_file(rng: random.Random, idx: int) -> str:
    parts = [f"package com.example.svc{idx % 5};", ""]
    parts.append("@RestController")
    parts.append(f"public class Controller_{idx} {{")
    for m in range(rng.randint(2, 5)):
        parts.append(f'  @GetMapping("/rest/service{idx}/item{m}")')
        parts.append(f"  public int get_{m}(int id) {{")
        parts.append(f"    return id + {rng.randint(1, 99)};")
        parts.append("  }")
        parts.append("")
    parts.append("}")
    return "\n".join(parts) + "\n"


def _yaml_file(rng: random.Random, idx: int) -> str:
    parts = [f"service: svc{idx}", "config:"]
    parts.append(f"  baseUrl: http://svc{rng.randint(0, 9)}.internal:8080")
    parts.append(f"  endpoint: /rest/service{rng.randint(0, 20)}/item{rng.randint(0, 9)}")
    for k in range(rng.randint(3, 8)):
        parts.append(f"  key_{k}: value_{rng.randint(0, 999)}")
    return "\n".join(parts) + "\n"


_GENERATORS = {
    "py": _py_file,
    "ts": _ts_file,
    "go": _go_file,
    "java": _java_file,
    "yaml": _yaml_file,
}


def _pick_ext(rng: random.Random) -> str:
    population = [ext for ext, _ in _LANGUAGE_WEIGHTS]
    weights = [w for _, w in _LANGUAGE_WEIGHTS]
    return rng.choices(population, weights=weights, k=1)[0]


def generate_corpus(
    root: Path,
    n_files: int = 300,
    seed: int = 1234,
    project_name: str = "benchproj",
    n_dirs: int = 12,
) -> CorpusStats:
    """Generate a deterministic synthetic project under ``root/project_name``.

    Returns CorpusStats describing what was written.
    """
    rng = random.Random(seed)
    project_root = root / project_name
    project_root.mkdir(parents=True, exist_ok=True)

    files_by_ext: dict[str, int] = {}
    total_bytes = 0

    for i in range(n_files):
        ext = _pick_ext(rng)
        sub = f"pkg_{i % n_dirs}/mod_{(i // n_dirs) % 5}"
        d = project_root / sub
        d.mkdir(parents=True, exist_ok=True)
        content = _GENERATORS[ext](rng, i)
        fpath = d / f"file_{i}.{ext}"
        fpath.write_text(content, encoding="utf-8")
        files_by_ext[ext] = files_by_ext.get(ext, 0) + 1
        total_bytes += len(content.encode("utf-8"))

    return CorpusStats(
        root=str(root),
        project_name=project_name,
        n_files=n_files,
        files_by_ext=files_by_ext,
        total_bytes=total_bytes,
    )
