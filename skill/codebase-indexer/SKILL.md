---
name: codebase-indexer
description: >
  Token-efficient code navigation using the codebase-indexer MCP tools (semantic
  search, symbol lookup, file outlines, project summaries). Invoke whenever the
  user wants to search code, find where a symbol is defined, understand how
  something works, trace a call chain, list all usages, explore a project's
  structure, or navigate an indexed codebase -- even if they don't mention
  "search" or "indexer" explicitly. Also invoke for prompts like "how does X
  work", "where is Y defined", "show me the structure", "find all callers of Z",
  "what files use this interface", or any question that requires understanding
  code across multiple files. Use this skill as the primary strategy for any
  code-reading task on an indexed project.
---

# Codebase Indexer -- Token-Efficient Code Navigation

You have MCP tools for semantic code search backed by a local Qdrant vector DB.
They vary widely in token cost. Follow the tool ladder below, stopping as soon
as you have enough information to answer the user.

## On Skill Load — Auto-Index Current Repository

When this skill is invoked, immediately run the following bootstrap sequence
**before** answering any user question:

1. Determine the current working directory basename (e.g. `my-project` from `C:\Users\me\repos\my-project`).
2. Call `list_collections()` to see what is already indexed.
3. If the current repo is **not** in the list → call `index_codebase(path="<basename>")` automatically, without asking the user. Inform the user that indexing has started and poll `index_status` until done.
4. Once indexed (or if it was already indexed) → call `get_collection_summary(collection="<basename>")` to orient.

Do this proactively every time the skill loads. The user should not have to ask.

## The Tool Ladder

Work top-to-bottom. Each step costs more tokens than the one above it.

```
1. get_collection_summary(collection="...")
   -> instant project orientation: language breakdown, directory tree, top files
   -> zero embedding cost; call first on any unfamiliar project

2. search_symbols(query="...", collection="...")
   -> find WHERE symbols live -- no code content returned
   -> ~90% fewer tokens than search_codebase; prefer this for "find the function" tasks

3. get_file_outline(rel_path="...", collection="...")
   -> symbol tree for a file -- know what's inside before loading any content
   -> zero embedding cost; use after search_symbols surfaces a relevant file

4. search_codebase(..., max_content_chars=300)
   -> returns partial chunk content (first 300 chars) + content_truncated flag
   -> use to confirm relevance across several candidates cheaply

5. get_chunk(chunk_id="...")
   -> full content of one chunk -- use only for the 1-2 chunks you actually need
```

**Never call `search_codebase` without `max_content_chars`** when you only need
to locate something. Full-content results are up to 150 lines each and burn
tokens fast. Use `search_symbols` first -- it answers "where is X?" at ~10% the
cost.

## Tool Quick Reference

| Tool | Token Cost | Best For |
|------|-----------|----------|
| `get_collection_summary` | Zero embed | First call; project orientation |
| `list_collections` | Zero embed | Discover what's indexed |
| `search_symbols` | Embed, no content | Locating symbols by name/concept |
| `get_file_outline` | Zero embed | File structure before reading |
| `search_codebase` + `max_content_chars` | Embed + partial | Narrowing candidates |
| `get_chunk` | Zero embed | Reading one specific chunk in full |
| `search_codebase` (no truncation) | Embed + full content | Last resort only |
| `find_cross_references` | Zero embed (member-only); embed with query/symbol_name | Precise call sites via member/receiver + cross-project links; ColBERT rerank when `RERANK_ENABLED=true` |
| `map_service_dependencies` | Multiple embeds | Full microservice call graph; ColBERT rerank when `RERANK_ENABLED=true` |
| `recommend_code` | Embed per text example | "Like this, not that" discovery via Qdrant Recommendation API (dense-only) |

## Common Patterns

### "Where is X defined?"
```
search_symbols(query="X", collection="project")
-> get_chunk(chunk_id) for the top result
```

### "How does X work? Walk me through it."
```
search_symbols(query="X")
-> get_file_outline(rel_path from result)
-> get_chunk(chunk_ids for relevant methods, one at a time)
```

### "Show me the structure / give me an overview"
```
get_collection_summary(collection="project")
```
That's often enough on its own. Resist the urge to follow up with searches.

### "Find all callers / usages of X"
```
# Primary: member-only (no symbol_name) — exact call sites
find_cross_references(collections=["project"], member="<method>", receiver="<field>")
-> read results where match_type == "call_site"
-> receiver is optional; use it to disambiguate inherited/Spring bean fields
-> do NOT pass symbol_name unless you want definition/import noise or code_dependency links

# Optional: symbol_name adds call sites plus links[] to the type definition
find_cross_references(collections=["project"], symbol_name="<TypeOrService>", member="<method>", receiver="<field>")

# Reindex if collection predates callees payload: index_codebase(path="project", force=True)

search_symbols(query="<method> call invocation", collection="project")   <- fallback only
search_codebase(query="<method>(", collection="project", max_content_chars=200)   <- semantic fallback
```

### "Find code like X but not Y"
```
# Use chunk IDs from a prior search, or positive/negative text queries
recommend_code(
  collection="project",
  positive_query="handler pattern similar to OrderService",
  negative_query="test utilities mock fixtures",
  path_glob="project/src/**/*.py",
  limit=5,
  max_content_chars=200,
)
-> get_chunk for the top 1-2 results
```

### Starting fresh -- project not yet indexed
```
list_collections()                          <- check what exists
index_codebase(path="<folder-basename>")    <- NOT "/" or a full path
index_status(collection="...")              <- poll until status="done"
get_collection_summary(collection="...")    <- then orient
```

### Searching across multiple projects
```
search_codebase(query="...", collections=["project-a", "project-b"])
find_cross_references(query="...", collections=["project-a", "project-b"])
map_service_dependencies(collections=["project-a", "project-b"])
```

## Path Conventions

- `index_codebase(path=...)` takes the **folder basename only** -- e.g. `my-project`,
  not `C:\Users\me\repos\my-project` and never `/`.
- After indexing, `rel_path` values in results are **prefixed** with the collection
  name: `my-project/src/main.py`. Use the full prefixed path for `get_file_outline`.
- **`path_glob`** on `recommend_code` must match indexed **`rel_path`** including the collection prefix (e.g. `my-project/src/**/*.py`, not bare `src/**/*.py`).

## When to Skip Ahead

- User asks a direct question about a specific file they name -> go straight to
  `get_file_outline`, then `get_chunk`.
- User just needs a file list or language stats -> `get_collection_summary` is
  the complete answer.
- User asks about cross-service HTTP calls -> jump to `map_service_dependencies`.
- User wants similar code excluding tests or legacy folders -> `recommend_code` with `path_glob` and optional negative examples.
- Collection doesn't exist -> index first, then re-enter the ladder at step 1.
