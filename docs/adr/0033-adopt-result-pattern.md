# 0033. Adopt Result pattern for expected failures

- **Status:** Proposed
- **Date:** 2026-07-21
- **Deciders:** Maintainers
- **Related:** [0030](0030-migrate-mcp-server-to-dotnet10.md) (.NET Domain/Application/Host layers), [0031](0031-mcp-liveness-vs-readiness.md) (dependency failure surfacing), [0032](0032-replace-magic-strings-with-enums.md) (typed vocabularies), [project-phase.md](../../.cursor/agents/project-phase.md) (pre-release: no backward compatibility)

## Context

The .NET MCP stack mixes several failure styles:

| Style today | Examples | Problem |
|-------------|----------|---------|
| Domain exceptions | `EmbeddingException`, `VectorStoreException`, `IndexCancelledException` | Callers must know which throws; easy to swallow or miss |
| Catch → string list | `IndexCodebaseService` → `PipelineResult.Errors` | Untyped; no code/category; hard to branch on |
| Nullable “not found” | `GetChunkByIdAsync` → `ChunkPayload?`, `GetJobAsync` → null | Ambiguous vs empty vs error; Host invents response DTOs |
| Ad-hoc MCP error objects | `IndexJobNotFoundResponse`, `IndexPathRequiredResponse`, … | Parallel error shapes; `Task<object>` tools; no shared contract |
| `InvalidOperationException` | job already running in `IndexJobService` | Control-flow exceptions for expected conflicts |

ASP.NET / library boundaries still need exceptions for bugs and cancellation, but **expected domain/application failures** (validation, not found, conflict, dependency down, embed/store reject) should be **values** that compose without try/catch.

### Hard constraints

- **Pre-release** — no dual APIs (no `Try*` + throw twins, no obsolete exception wrappers kept “for callers”). Break port signatures and MCP error JSON as needed.
- **Domain stays dependency-free** — Result types live in `CodebaseIndexer.Domain` (same rule as ADR 0030: Domain has no NuGet packages). Do **not** pull FluentResults / ErrorOr / OneOf into Domain.
- **Cancellation stays exceptional** — `OperationCanceledException` / cooperative cancel via `CancellationToken` is not wrapped in `Result`.

### Requirements and goals

- One **`Result` / `Result<T>`** (and a small **`Error`**) used by Application services and Domain ports for expected failures.
- Host MCP tools map `Result` failures to a **single error payload shape** (plus success DTOs); stop growing one-off `*ErrorResponse` records except where a success-shaped message is still useful.
- Unexpected bugs and infra panics may still throw; infrastructure adapters **catch external SDK failures** and return `Result` (or map at the Application edge) instead of leaking raw SDK exceptions through ports.
- Exhaustive handling at boundaries: prefer pattern match / `Match` / early `if (result.IsFailure)` — no silent discard of errors.

### Why now

Ports and Application services are still being shaped under [0030](0030-migrate-mcp-server-to-dotnet10.md). Introducing Result before more MCP tools and graph/search paths land avoids a second retrofit of every `Task<T?>` / throw site.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Domain / Application API design | yes | Primary |
| MCP tool error JSON | yes | Breaking OK; one envelope |
| Retrieval ranking | no | Unchanged |
| Python `mcp_server/` | no | Legacy delete path |

## Decision

We will **implement a lightweight Domain-owned Result pattern** and use it for **expected failures** across Domain ports and Application services. Host maps failures to MCP responses. **No backward-compatible exception or nullable APIs** for those paths.

### Core types (sketch)

```csharp
// CodebaseIndexer.Domain — one type per file
public enum ErrorKind { Validation, NotFound, Conflict, Dependency, Transient, Cancelled /* rare */, Internal }

public sealed record Error(ErrorKind Kind, string Code, string Message, IReadOnlyDictionary<string, string>? Metadata = null);

public readonly struct Result
{
    public bool IsSuccess { get; }
    public Error Error { get; } // valid only when !IsSuccess
    public static Result Success();
    public static Result Failure(Error error);
}

public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }     // valid only when IsSuccess
    public Error Error { get; } // valid only when !IsSuccess
    public static Result<T> Success(T value);
    public static Result<T> Failure(Error error);
}
```

Implementation details (readonly struct vs sealed class, `Match` helpers, implicit conversions) are left to Phase 1 — keep the API small and allocation-conscious on hot paths.

### Failure vs exception policy

| Situation | Use |
|-----------|-----|
| Validation / bad tool args | `Result` + `ErrorKind.Validation` |
| Missing collection, job, chunk | `Result` + `NotFound` (replace `T?`) |
| Job already running / illegal state transition | `Result` + `Conflict` |
| TEI / Qdrant / Neo4j unreachable or reject | `Result` + `Dependency` or `Transient` |
| Partial pipeline step failure (batch embed) | Prefer `Result` per step or typed error entries — **not** untyped `List<string>` long-term |
| `CancellationToken` canceled | Throw `OperationCanceledException` (do not encode as success Result) |
| Programmer bug / invariant broken | Throw (or `ErrorKind.Internal` only at outermost Host catch) |

### Existing domain exceptions

- **Remove or stop using** `EmbeddingException` / `VectorStoreException` on port boundaries once adapters return `Result`.
- `IndexCancelledException`: prefer cancel via token; if a sync cancel signal remains, map once at the job layer to a terminal job status — do not proliferate cancel-as-Result across every call.

### MCP Host mapping

- Prefer typed returns over `Task<object>` where the SDK allows; otherwise map:

  - `Result.Success` → existing success DTO / snapshot
  - `Result.Failure` → **one** envelope, e.g. `{ "error": { "kind", "code", "message", "metadata?" } }`

- Delete redundant one-off error records as tools are migrated (`IndexJobNotFoundResponse`, path-required, etc.) unless they carry **non-error** UX fields that belong on a success-shaped message DTO.

### In scope

- `Result` / `Result<T>` / `Error` / `ErrorKind` in Domain
- Application services + Domain ports that express expected failure
- Infrastructure adapters mapping SDK failures → `Result`
- Host MCP tools + unit/integration tests
- Replace nullable not-found and control-flow throws on those surfaces

### Out of scope

- Changing Qdrant/TEI/Neo4j product behavior
- Result for every private helper (only meaningful boundaries)
- Railway-oriented mega-framework / NuGet Result libraries in Domain
- Wrapping every `catch (Exception)` into `Internal` without fixing root cause
- Python error shapes

### Default behavior and configuration

- *Default:* **breaking** — ports and tools return `Result` / error envelope; no feature flag
- *Configuration surface:* none

### Phased delivery

1. **Phase 1 — Core types + conventions** — add `Result`/`Error`/`ErrorKind`; document policy in Domain XML docs; unit tests for success/failure/`Match`; pick struct vs class.
2. **Phase 2 — Application + index/job ports** — `IIndexJobService`, index pipeline / embed orchestration return `Result`; replace job conflict throws and string-only error bags with typed errors (pipeline summary may still list multiple `Error`s).
3. **Phase 3 — Storage / embed ports + MCP tools** — `IVectorStore` / embedders / search services use `Result` for expected failures; Host maps to unified error envelope; delete obsolete exception types and one-off error DTOs; Docker integration.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Chosen: Domain-owned `Result`/`Error`** | No Domain NuGet; fits ADR 0030; explicit policy | Small amount of hand-rolled API |
| Status quo (exceptions + null + ad-hoc DTOs) | Zero work | Inconsistent; hard to compose |
| FluentResults / ErrorOr package | Rich API | Domain dependency; heavier than needed |
| Exceptions only + global MCP filter | Familiar | Expected control flow via exceptions; weak typing at tools |
| `bool Try*` + out params everywhere | No exceptions | Noisy; no rich error; poor async story |

## Consequences

### Positive

- Expected failures are visible in signatures (`Result<T>` vs `T` / `T?`)
- One MCP error envelope; easier client handling
- Adapters stop leaking SDK exceptions through ports
- Aligns with enum hardening ([0032](0032-replace-magic-strings-with-enums.md)) via `ErrorKind` / stable `Code` strings

### Negative / trade-offs

- Breaking port and tool JSON changes
- More verbose call sites until helpers exist
- Risk of overusing Result for bugs (mitigate with clear policy table)

### Neutral / follow-ups

- Optional analyzers / review checklist: “no discard of `Result`”
- Metrics: count failures by `ErrorKind` / `Code` ([0018](0018-telemetry-observability-otel-prometheus.md))

### Downstream work

- Pairs with [0031](0031-mcp-liveness-vs-readiness.md) for dependency-down as `ErrorKind.Dependency` on tool calls while `/ready` fails closed

## Implementation notes

### New artifacts

- `src/CodebaseIndexer.Domain/Results/Result.cs`, `ResultT.cs` (or single file only if ADR 0030 one-type-per-file is waived for tiny twins — **prefer one type per file**)
- `Error.cs`, `ErrorKind.cs`
- Optional `ResultExtensions.Match` / `EnsureSuccess` (Host-only throw helper discouraged except tests)

### Modified artifacts

- Domain ports (`IVectorStore`, embedders, index pipeline as applicable)
- Application services (`IndexJobService`, `IndexCodebaseService`, search/collection services)
- Infrastructure adapters (Qdrant, TEI, sparse, graph)
- Host MCP tools; remove obsolete `*Exception` / one-off error responses
- Unit + Docker integration tests; CHANGELOG note for MCP error JSON

### Dependencies

- *Runtime:* none
- *Optional:* none

### Rollout

- **breaking** — merge by phase; clients adapt to error envelope

### Data migration

- none (behavior/API only; no re-index required unless combined with [0032](0032-replace-magic-strings-with-enums.md))

## Validation

### Automated tests

- *Unit* — Result success/failure invariants; service methods return expected `ErrorKind`/`Code` for not-found, validation, conflict; adapters map HTTP/transport failures to `Dependency`/`Transient`
- *Integration* — MCP tool call with bad path / missing collection returns unified error envelope; happy path unchanged functionally

### Success criteria

1. Migrated ports/services do not use `T?` or domain exceptions for the expected-failure cases listed in the policy table
2. MCP tools share one failure envelope; obsolete one-off error DTOs removed for migrated tools
3. Docker integration passes; CHANGELOG documents breaking error JSON
