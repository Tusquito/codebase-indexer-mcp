# 0032. Replace closed-set magic strings with domain enums

- **Status:** Accepted
- **Date:** 2026-07-21
- **Deciders:** Maintainers
- **Related:** [0030](0030-migrate-mcp-server-to-dotnet10.md) (.NET Domain / MCP surface), [0003](0003-hybrid-search-rrf-default.md) (named vectors), [0004](0004-collection-per-project-isolation.md) (payload schema), [project-phase.md](../../.cursor/agents/project-phase.md) (pre-release: no backward compatibility)

## Context

Across the .NET Domain, indexing, search, and MCP tool layers, **closed vocabularies are still plain `string`s** compared and returned with literal literals. Examples today:

| Vocabulary | Representative literals | Where |
|------------|-------------------------|--------|
| Symbol kind | `"function"`, `"class"`, `"method"`, `"other"`, `"config"`, `"manifest"`, `"ops"`, `"type"`, SQL kinds (`"table"`, …) | `Chunk.SymbolType`, `FileSymbol`, `ChunkPayload`, `TreeSitterChunker`, `ChunkerCore`, `QdrantVectorStore` |
| Source language | `"python"`, `"csharp"`, `"yaml"`, … | `Chunk.Language`, `LanguageRegistry`, import/header processors |
| Named vector | `"dense"`, `"sparse"` (and ColBERT when enabled) | `QdrantVectorStore` create/query/upsert |
| Reference / match kind | `"definition"`, `"import"`, `"usage"`, `"call_site"`, `"semantic"`, … | `ReferenceClassifier`, cross-ref / search DTOs |
| Liveness wire status | `"ok"`, `"unhealthy"` | `McpHostHealthCheck` JSON |

`IndexJobStatus`, `EmbedRole`, `TruncationSource`, and `MemoryPressureSeverity` already show the intended pattern: **typed enums in Domain (or a narrow Infrastructure concern), not scattered string constants.**

### Hard constraints

- **Pre-release** — no backward compatibility with prior payload string shapes, MCP JSON enum casing, or dual string/enum APIs ([project-phase](../../.cursor/agents/project-phase.md)).
- **Re-index after cutover** — Qdrant payload fields and named-vector names that change require `index_all(force=true)` (or equivalent); no schema-version env knobs.
- **Python `mcp_server/`** is on a delete path under [0030](0030-migrate-mcp-server-to-dotnet10.md); this ADR targets the **.NET** codebase as source of truth.

### Requirements and goals

- Every **closed, app-owned vocabulary** used in Domain models, ports, classifiers, and MCP response DTOs is an `enum` (one type per file per ADR 0030 convention).
- Comparisons, switches, and defaults use enum members — not `"other"` / `"dense"` string literals in business logic.
- Wire serialization (MCP JSON, Qdrant payload values) is **explicit and centralized** (`JsonStringEnumConverter` and/or small mapping helpers), not ad-hoc `ToString()` / magic defaults.
- Open-ended text (paths, symbol names, chunk content, collection names, free-form queries) stays `string`.

### Why now

ADR 0030 Domain models are still early (`Chunk`, `FileSymbol`, `ChunkPayload` use `string SymbolType` / `string Language`). Fixing vocabularies before more MCP tools and graph ports land avoids a large retrofit and eliminates typo-driven bugs (`"methode"` vs `"method"`) at compile time.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Domain / type safety | yes | Primary goal |
| MCP tool JSON contract | yes | Breaking OK; document new enum wire forms |
| Qdrant payload / named vectors | yes | Re-index required |
| Retrieval ranking quality | no | Same semantics after re-index |
| Python parity | no | Legacy path; not maintained for this change |

## Decision

We will **replace closed-set magic strings with Domain (and, where vector/API-bound, Infrastructure) enums**, serialize them explicitly at boundaries, and **require a full re-index** after the change. No dual-write, no string fallbacks, no legacy aliases.

### Canonical enums (minimum set)

| Enum | Members (initial; extend as classifiers need) | Home |
|------|-----------------------------------------------|------|
| `SymbolType` | `Function`, `Class`, `Method`, `Other`, `Type`, `Config`, `Manifest`, `Ops`, plus SQL kinds as needed (`Table`, `Procedure`, `View`, `Trigger`, `Index`) | `CodebaseIndexer.Domain` |
| `SourceLanguage` | Closed set aligned with `LanguageRegistry` ids (`Python`, `JavaScript`, `TypeScript`, `CSharp`, `Yaml`, …) | `CodebaseIndexer.Domain` |
| `NamedVector` | `Dense`, `Sparse`, `Colbert` (if used) | Domain or Infrastructure next to Qdrant |
| `ReferenceType` | `Definition`, `Import`, `Usage`, `EndpointDefinition`, `HttpCall`, `CallSite`, … | `CodebaseIndexer.Domain` |
| `MatchType` | `Semantic`, `ExactSymbol`, `ImportSearch`, `CallSite`, … | `CodebaseIndexer.Domain` |
| `LivenessStatus` (or reuse existing health model) | `Ok`, `Unhealthy` | Application/Host as appropriate |

Existing enums (`IndexJobStatus`, `EmbedRole`, …) stay; do not reintroduce string twins.

### Serialization policy

- Prefer **`[JsonConverter(typeof(JsonStringEnumConverter))]`** (or global options) with a **single naming policy** for MCP JSON (document the chosen policy in implementation — e.g. camelCase member names matching today’s `"function"` / `"call_site"` via `[EnumMember]` / `[JsonStringEnumMemberName]` where snake_case is required).
- Qdrant payload: store the **canonical wire string** for each enum (same mapping as MCP) so scroll/filter and tool responses stay consistent.
- Named vectors: map `NamedVector` → Qdrant vector name string in **one** helper used by create/query/upsert; do not scatter `"dense"` literals.

### In scope

- Domain model property types (`Chunk`, `FileSymbol`, `ChunkPayload`, search/cross-ref DTOs)
- Chunker / language registry / reference classifier return types
- `QdrantVectorStore` payload read/write and named-vector usage
- MCP tool response models and unit/integration tests
- Operator note: re-index after upgrade

### Out of scope

- Open strings: `RelPath`, `SymbolName`, `Content`, collection names, queries
- Tree-sitter **AST node type** strings from grammars (map *into* `SymbolType`; do not enum every grammar node)
- HTTP header / env / config **key** names
- Python `mcp_server/` string cleanup (deleted under ADR 0030 cutover)
- Preserving old collections without re-index

### Default behavior and configuration

- *Default:* **breaking** — enums only; invalid/unknown payload values fail closed or map to a single documented fallback member (prefer `Other` only for `SymbolType` where historically used)
- *Configuration surface:* none (no feature flag, no schema version env)

### Phased delivery

1. **Phase 1 — Domain enums + model/port signatures** — introduce enums; change Domain records and Application ports/DTOs; wire JSON converters; update unit tests that construct models.
2. **Phase 2 — Indexing + Qdrant** — chunker/classifiers return enums; vector store maps enums ↔ payload/named vectors; Docker integration; document **re-index required**.
3. **Phase 3 — Search / cross-ref / health** — `ReferenceClassifier`, match types, liveness JSON; remaining MCP tool surfaces and tests.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Chosen: Domain enums + centralized wire mapping** | Compile-time safety; one vocabulary; fits ADR 0030 | Breaking re-index; mapping boilerplate at boundaries |
| Status quo (`string` + literals) | Zero migration | Typos, drift across layers, weak IDE/refactor support |
| `static class` string constants only | Cheap; shared literals | Still `string`-typed; no exhaustiveness in `switch` |
| Keep strings internally; enum only at MCP edge | Smaller Domain diff | Classifiers/storage still magic-string prone |
| Dual string+enum / alias tables for old payloads | Soft upgrade | Forbidden by pre-release policy; ongoing drift |

## Consequences

### Positive

- Exhaustive switches and IDE rename/refactor across SymbolType / language / reference kinds
- MCP and Qdrant boundaries share one mapping story
- Aligns new .NET code with existing `IndexJobStatus` / `EmbedRole` style

### Negative / trade-offs

- Breaking: existing indexed payloads and any clients hard-coding string sets must re-index / adapt
- Enum ↔ string mapping must be tested (round-trip upsert + tool JSON)
- Adding a new language or symbol kind requires an enum member + registry update (intentional friction)

### Neutral / follow-ups

- Update SKILL / copilot-instructions examples if they cite symbol_type string lists
- After Phase 2, force re-index in CHANGELOG operator notes

### Downstream work

- Continues [0030](0030-migrate-mcp-server-to-dotnet10.md) Domain hardening
- May simplify filters in search tools once `SymbolType` / `SourceLanguage` are typed

## Implementation notes

### New artifacts

- `src/CodebaseIndexer.Domain/Models/SymbolType.cs` (and siblings listed above)
- Optional `NamedVectorNames` / `EnumWireFormat` helper for Qdrant + JSON

### Modified artifacts

- `Chunk`, `FileSymbol`, `ChunkPayload`, related Application models
- `TreeSitterChunker`, `ChunkerCore`, `LanguageRegistry`, `ImportHeaderProcessor`
- `QdrantVectorStore`, `ReferenceClassifier`, health JSON writers
- Unit + Docker integration tests; CHANGELOG / DEPLOYMENT re-index note

### Dependencies

- *Runtime:* none new (System.Text.Json enum converters)
- *Optional:* none

### Rollout

- **breaking** — land behind normal PR; operators re-index after pull

### Data migration

- **yes** — full re-index (`index_all` / force) so payload `symbol_type`, `language`, and named vectors match the new canonical wire form; no in-place payload rewrite job

## Validation

### Automated tests

- *Unit* — enum round-trip JSON; classifier returns enum members; chunker defaults to `SymbolType.Other`; Qdrant mapping helper covers all `NamedVector` values
- *Integration* — compose: index sample project, `search_symbols` / `get_collection_summary` expose expected symbol_type / language wires; no raw unknown strings in summaries

### Success criteria

1. Domain models for closed vocabularies use enums; production code paths do not compare those fields to string literals
2. Named vector and payload write/read go through a single mapping surface
3. Docker integration passes after re-index; CHANGELOG states re-index required
