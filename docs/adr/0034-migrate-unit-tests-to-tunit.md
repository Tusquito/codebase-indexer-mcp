# 0034. Adopt TUnit as the full .NET test stack

- **Status:** Proposed
- **Date:** 2026-07-22
- **Deciders:** Maintainers
- **Related:** [0030](0030-migrate-mcp-server-to-dotnet10.md) (.NET `test/` projects), [TUnit intro](https://tunit.dev/docs/intro), [Assertions](https://tunit.dev/docs/assertions/getting-started), [Things to know](https://tunit.dev/docs/writing-tests/things-to-know), [TUnit.Mocks](https://tunit.dev/docs/writing-tests/mocking/), [Mock setup](https://tunit.dev/docs/writing-tests/mocking/setup), [Aspire integration](https://tunit.dev/docs/examples/aspire), [Code coverage](https://tunit.dev/docs/extending/code-coverage), [Migrate from xUnit](https://tunit.dev/docs/migration/xunit), [Framework differences](https://tunit.dev/docs/comparison/framework-differences), [Microsoft.Testing.Platform](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-intro), [project-phase.md](../../.cursor/agents/project-phase.md) (pre-release: no backward compatibility)

## Context

All .NET unit/integration tests under `test/` use **xUnit 2.9.x** on **VSTest** (`Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio`), centralized in [`test/Directory.Build.props`](../../test/Directory.Build.props). Six projects share that stack (~150+ `[Fact]` / `[Theory]` sites):

| Project | Role today |
|---------|------------|
| `CodebaseIndexer.Domain.Tests` | Domain models / enums |
| `CodebaseIndexer.Application.Tests` | Application services (many hand-rolled `Fake*` / `Stub*` doubles) |
| `CodebaseIndexer.Infrastructure.Tests` | Chunker, Qdrant helpers, TEI/BM25, Neo4j fakes |
| `CodebaseIndexer.Host.Tests` | ASP.NET Core host smoke + health (`WebApplicationFactory`, `IClassFixture`) |
| `CodebaseIndexer.AppHost.Tests` | Aspire AppHost (placeholder + `Aspire.Hosting.Testing`; no real distributed tests yet) |
| `CodebaseIndexer.Proxy.Tests` | Stdio proxy (duplicates package versions → NU1504) |

CI runs `dotnet test CodebaseIndexer.slnx`. Coverage uses **Coverlet** (`coverlet.collector`), which only works with VSTest.

A mechanical attribute swap (`[Fact]` → `[Test]`) would leave half the value on the table. **[TUnit](https://tunit.dev/docs/intro)** is a full testing platform on **Microsoft.Testing.Platform (MTP)**:

| Capability | TUnit surface | Replaces / avoids |
|------------|---------------|-------------------|
| Runner + discovery | Source-generated engine on MTP | VSTest + `Microsoft.NET.Test.Sdk` |
| Assertions | Built-in fluent [`Assert.That`](https://tunit.dev/docs/assertions/getting-started) (always `await`) | xUnit `Assert.*`, FluentAssertions, Shouldly |
| Mocking | [`TUnit.Mocks`](https://tunit.dev/docs/writing-tests/mocking/) (source-gen, AOT-friendly) | Moq / NSubstitute / ad-hoc stubs for interfaces |
| Host testing | TUnit fixtures + optional `TUnit.AspNetCore` | xUnit `IClassFixture` only |
| Aspire | [`TUnit.Aspire`](https://tunit.dev/docs/examples/aspire) `AspireFixture<T>` | Hand-rolled `DistributedApplicationTestingBuilder` boilerplate |
| Coverage | Built-in [`Microsoft.Testing.Extensions.CodeCoverage`](https://tunit.dev/docs/extending/code-coverage) via `--coverage` | Coverlet (incompatible with MTP) |
| Parallelism / isolation | Parallel by default; **new class instance per test** ([things to know](https://tunit.dev/docs/writing-tests/things-to-know)) | xUnit parallel + shared-instance pitfalls |

### Hard constraints

- **Pre-release** — no dual stacks. Cut over fully; delete xUnit, VSTest SDK, Coverlet, and third-party assertion/mock libraries.
- **Same primary CLI** — `dotnet test CodebaseIndexer.slnx` remains the documented/CI entrypoint (MTP under the hood). Coverage uses `--coverage` (and/or `dotnet run --project <tests> -- --coverage` per TUnit docs).
- **Product behavior unchanged** — this ADR changes the **test stack**, not MCP/retrieval semantics.
- **Python `benchmarks/`** stay on pytest ([0030](0030-migrate-mcp-server-to-dotnet10.md)).
- **C# 14** — required by `TUnit.Mocks` (`T.Mock()` / extension properties); already implied by .NET 10 SDK.

### Requirements and goals

1. **Full TUnit stack** — use the library’s first-party capabilities end-to-end, not a minimal xUnit clone.
2. **Assertions** — only TUnit assertions (`await Assert.That(...)`); prefer `.And` / `Assert.Multiple()` / `.Member()` / collection helpers over ad-hoc checks; never forget `await` (silent pass).
3. **Mocking** — prefer `TUnit.Mocks` for Domain/Application **ports and interfaces**; use `TUnit.Mocks.Http` / `TUnit.Mocks.Logging` where HTTP/`ILogger` doubles are needed.
4. **Aspire** — AppHost integration tests use `TUnit.Aspire` (`AspireFixture<Projects.CodebaseIndexer_AppHost>`, `SharedType.PerTestSession`), not raw Aspire testing boilerplate.
5. **Coverage** — MTP built-in coverage only; Coverlet removed.
6. **Correct TUnit idioms** — parallel by default; no instance-field state across tests; shared state via `static` or `[ClassDataSource<>]` / session fixtures; `[NotInParallel]` only when required ([things to know](https://tunit.dev/docs/writing-tests/things-to-know)).
7. **Faster CI / local feedback** from source-gen discovery + MTP.

### Why now

- .NET 10 runtime migration ([0030](0030-migrate-mcp-server-to-dotnet10.md)) is largely landed; test volume and AppHost coverage are about to grow.
- AppHost tests are still placeholders — adopting `TUnit.Aspire` **before** writing real distributed tests avoids a second rewrite.
- Application tests already sprawl hand-rolled fakes; introducing `TUnit.Mocks` now prevents Moq/NSubstitute from becoming the de-facto choice.
- Coverlet + VSTest is a dead end once MTP is the runner.

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Unit / host / AppHost test harness | yes | Primary |
| CI `dotnet test` + optional `--coverage` | yes | |
| Product MCP / retrieval behavior | no | Unchanged |
| Python `benchmarks/` pytest | no | Keep |
| Docker compose integration harness | partial | Remains; AppHost TUnit tests may complement, not replace, phase Docker gates |

## Decision

We will **adopt TUnit as the sole .NET test stack** under `test/`: runner (MTP), **built-in assertions**, **TUnit.Mocks** (+ Http/Logging as needed), **TUnit.Aspire** for AppHost integration tests, and **built-in MTP code coverage**. We will **not** keep xUnit, Coverlet, FluentAssertions/Shouldly, or Moq/NSubstitute.

This is a **full-capability adoption**, not an attribute-only migration. New and migrated tests follow TUnit’s documented patterns ([assertions](https://tunit.dev/docs/assertions/getting-started), [mocking](https://tunit.dev/docs/writing-tests/mocking/setup), [Aspire](https://tunit.dev/docs/examples/aspire), [things to know](https://tunit.dev/docs/writing-tests/things-to-know), [coverage](https://tunit.dev/docs/extending/code-coverage)).

### Package matrix

| Package | Where | Purpose |
|---------|-------|---------|
| `TUnit` | all `test/**/*.Tests` via `Directory.Build.props` | Core + engine + assertions + MTP coverage extension |
| `TUnit.Mocks` | shared props (all test projects) | Source-generated mocks for ports/interfaces |
| `TUnit.Mocks.Http` | Host / Infrastructure as needed | `HttpClient` / handler doubles (e.g. TEI stubs) |
| `TUnit.Mocks.Logging` | as needed | `ILogger` capture/verify |
| `TUnit.Aspire` | `CodebaseIndexer.AppHost.Tests` | `AspireFixture<T>` lifecycle |
| `TUnit.AspNetCore` | `CodebaseIndexer.Host.Tests` (if useful) | Prefer over inventing a second host-fixture pattern; otherwise keep `WebApplicationFactory` + TUnit `[ClassDataSource]` |

**Remove:** `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`, direct `Aspire.Hosting.Testing` usage in AppHost tests once `TUnit.Aspire` owns the fixture (transitive Aspire testing bits may remain via `TUnit.Aspire`).

Pin **latest stable** versions at implementation time (`dotnet add package`).

### How to write tests (canonical conventions)

From [Things to know](https://tunit.dev/docs/writing-tests/things-to-know) and first-party docs — **required** in this repo:

1. **Parallel by default** — assume concurrent execution. Use `[NotInParallel]` (optionally with `Order`) only for proven shared-resource conflicts (e.g. exclusive temp dirs, single Docker port). Prefer isolating resources over serializing the suite.
2. **New instance per test** — do **not** share mutable instance fields across tests. Need shared setup → `[ClassDataSource<T>(Shared = …)]`, session-scoped Aspire fixture, or explicit `static` (rare; document why).
3. **Always `await` assertions** — unawaited `Assert.That(...)` never runs and the test passes silently. Prefer analyzers on; treat missing await as a defect.
4. **Fluent assertions only** — `await Assert.That(actual).IsEqualTo(expected)`; use `.And`, `.Or`, `Assert.Multiple()`, `.Member()`, collection `IsEquivalentTo` / `All` / `Any` instead of multi-statement xUnit-style asserts when it clarifies failure output.
5. **Mocks over hand-rolled stubs for interfaces** — `var mock = IDenseEmbedder.Mock(); mock.EmbedAsync(Any()).Returns(...);` Prefer loose vs strict deliberately; consider assembly/discovery hook for default `MockBehavior` if the suite standardizes on Strict.
6. **Stateful / recording doubles** — keep small custom fakes only when the double’s value is **behavior recording** that mocks express poorly (e.g. Neo4j query capture lists). Prefer mocks + callbacks otherwise.
7. **Aspire** — one `AspireFixture` (or subclass) per session (`SharedType.PerTestSession`); customize via `ConfigureBuilder`, `ResourcesToRemove`, `ResourceTimeout`, `WaitBehavior`; use `CreateHttpClient`, `WatchResourceLogs`, telemetry correlation as documented.
8. **Host** — map `IClassFixture<McpHostWebApplicationFactory>` → `[ClassDataSource<McpHostWebApplicationFactory>(Shared = SharedType.PerClass)]` (or `TUnit.AspNetCore` equivalent); keep factory subclasses for config overrides.
9. **Data** — `[Arguments]`, `[MethodDataSource]`, matrix/data sources on methods **and** classes as needed; convert `object[]` MemberData to typed tuples during migration.

### Attribute / API mapping (xUnit → TUnit)

| xUnit | TUnit |
|-------|-------|
| `[Fact]` / `[Theory]` | `[Test]` |
| `[InlineData(...)]` | `[Arguments(...)]` |
| `[MemberData(nameof(...))]` | `[MethodDataSource(nameof(...))]` |
| `[Trait("k","v")]` | `[Property("k","v")]` |
| `IClassFixture<T>` | `[ClassDataSource<T>(Shared = SharedType.PerClass)]` |
| `Assert.Equal(e, a)` | `await Assert.That(a).IsEqualTo(e)` |
| `ITestOutputHelper` | `TestContext` |
| Hand-rolled interface stub | `T.Mock()` / `Mock.Of<T>()` |
| Coverlet collector | `--coverage` (MTP extension bundled with `TUnit`) |
| Raw Aspire testing builder | `AspireFixture<TAppHost>` |

Automated first pass: [migrate from xUnit](https://tunit.dev/docs/migration/xunit) (`TUXU0001` code fixers), then **manually** adopt mocks, Aspire fixtures, assertion idioms, and parallelism rules above.

### Coverage

- Coverlet is **not compatible** with MTP — remove it ([docs](https://tunit.dev/docs/extending/code-coverage)).
- Collect with `--coverage` (Cobertura default); optional `--coverage-output` / `--coverage-settings`.
- CI: keep `dotnet test` blocking; add coverage collection as a non-blocking or artifact-uploading step once reports are useful — do not gate PRs on coverage % until a baseline exists.

### Runner / IDE

- Ensure MTP is the runner for these projects; add `global.json` `"test": { "runner": "Microsoft.Testing.Platform" }` if needed for consistent CLI behavior.
- Document IDE MTP toggles (VS / Rider / C# Dev Kit) in CONTRIBUTING.

### In scope

- All six `test/**/*.Tests` projects + `test/Directory.Build.props`
- Assertions, mocks, Host fixtures, AppHost `TUnit.Aspire` tests, MTP coverage wiring
- Replace suitable hand-rolled interface fakes with `TUnit.Mocks` during migration (opportunistic in Phases 1–2; complete for new tests immediately)
- CI / CONTRIBUTING / copilot-instructions updates
- Delete Proxy duplicate PackageReferences

### Out of scope

- Production runtime changes (except tiny hooks already required by tests)
- Python `benchmarks/`
- Mandatory coverage %-gate on every PR (observation first)
- Native AOT publish of test assemblies (optional later)
- Third-party assertion/mock libraries
- Replacing the Docker compose / quality harness with Aspire-only testing (Aspire TUnit tests **complement** it)

### Default behavior and configuration

- *Default:* **breaking** for contributors (MTP + new packages/APIs)
- *Configuration surface:* optional `global.json` test runner; optional `coverage.config` / `testconfig.json` for coverage includes; no product feature flags

### Phased delivery

1. **Phase 1 — Core stack + unit projects**  
   Switch `Directory.Build.props` to `TUnit` + `TUnit.Mocks`; remove xUnit/Test.Sdk/Coverlet from shared props; migrate Domain / Application / Infrastructure via `TUXU0001`; rewrite assertions to awaited fluent form; convert straightforward interface fakes to `TUnit.Mocks`; enforce parallel/isolation conventions. `dotnet test` green for those three.

2. **Phase 2 — Host + Proxy**  
   Convert `IClassFixture` / factories; adopt `TUnit.AspNetCore` if it reduces boilerplate; `TUnit.Mocks.Http` for TEI/handler stubs where applicable; fix Proxy NU1504; full solution unit/host green.

3. **Phase 3 — Aspire + coverage + docs**  
   Add `TUnit.Aspire` to AppHost.Tests; replace placeholder/`Aspire.Hosting.Testing` boilerplate with `AspireFixture<Projects.CodebaseIndexer_AppHost>` (session-shared); smoke at least one real HTTP/resource health test against AppHost; wire `--coverage` in CI as artifacts; update CONTRIBUTING + copilot-instructions with the conventions above.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Full TUnit stack (chosen)** | One vendor surface; assertions + mocks + Aspire + coverage; source-gen; MTP-native | Migration + learning curve; younger ecosystem |
| Attribute-only TUnit (keep Moq/FluentAssertions/Coverlet) | Smaller change | Coverlet incompatible; fragments tooling; misses Aspire/mocks value |
| Stay on xUnit 2 + VSTest | Familiar | Misses MTP; Coverlet dead-end; AppHost boilerplate grows |
| xUnit v3 + MTP + Moq | Familiar asserts/facts | Still assemble a patchwork; no first-party Aspire fixture story like `TUnit.Aspire` |
| NUnit / MSTest | Mature | Wrong migration direction from current xUnit base |

## Consequences

### Positive

- Single coherent test platform (runner, assert, mock, Aspire, coverage).
- Faster discovery/execution; better parallel control (`--maximum-parallel-tests`, `[NotInParallel]`).
- Real AppHost integration tests become cheap to write via `AspireFixture`.
- Interface doubles become consistent and AOT-friendly; less nested `Fake*` noise.
- Coverage without Coverlet/VSTest glue.

### Negative / trade-offs

- Larger migration than “rename Fact → Test” (assertions, fixtures, mocks, Aspire).
- Contributors must learn awaited assertions and per-test instance semantics.
- IDE MTP toggle required for Test Explorer.
- Some recording fakes (Neo4j query lists) may remain custom — document when mocks are enough.

### Neutral / follow-ups

- Optional `TUnit.Assertions.Should` only if the team strongly prefers `value.Should()` syntax — default remains `Assert.That`.
- Optional Playwright package later if UI appears (not current).
- Tune coverage include/exclude once reports exist.

### Downstream work

- Unlocks faster test updates for [0031](0031-mcp-liveness-vs-readiness.md), [0033](0033-adopt-result-pattern.md), and future AppHost/Docker-adjacent work.

## Implementation notes

### New artifacts

- Optional `coverage.config` / `testconfig.json` for coverage filters
- AppHost `AspireFixture` subclass under `test/CodebaseIndexer.AppHost.Tests`
- CONTRIBUTING section: TUnit conventions (await asserts, parallelism, mocks, Aspire, coverage)

### Modified artifacts

- `test/Directory.Build.props` and all test `.csproj` / test sources
- `.github/workflows/ci.yml` (coverage artifact step when ready)
- `CONTRIBUTING.md`, `.github/copilot-instructions.md`

### Dependencies

- *Test (shared):* `TUnit`, `TUnit.Mocks` (latest stable)
- *Test (selective):* `TUnit.Mocks.Http`, `TUnit.Mocks.Logging`, `TUnit.Aspire`, optionally `TUnit.AspNetCore`
- *Remove:* `xunit*`, `Microsoft.NET.Test.Sdk`, `coverlet.*`, AppHost direct `Aspire.Hosting.Testing` once superseded

### Rollout

- **breaking** for local/IDE discovery and package restore; CI switches in the same PR stream as the cutover

### Data migration

- none (tests only)

## Validation

### Automated tests

- *Unit* — Domain / Application / Infrastructure pass on TUnit with awaited assertions
- *Mocks* — at least one Application service test uses `TUnit.Mocks` end-to-end (setup + verify)
- *Host* — smoke + health factory tests green with TUnit shared data sources
- *Aspire* — at least one `AspireFixture` test starts AppHost resources and asserts a health/HTTP signal (Docker required locally/CI where AppHost tests run)
- *Coverage* — `--coverage` produces Cobertura (or configured format) without Coverlet

### CI adoption

- Blocking: `dotnet test CodebaseIndexer.slnx`
- Coverage: upload artifacts first; %-gate only after baseline

### Success criteria

1. Zero PackageReferences to xUnit / Microsoft.NET.Test.Sdk / coverlet under `test/`
2. No FluentAssertions / Shouldly / Moq / NSubstitute dependencies
3. Pass count ≥ pre-migration; no dropped scenarios
4. Host smoke green; AppHost uses `TUnit.Aspire` (not placeholder-only)
5. Docs list TUnit conventions (await, isolation, mocks, Aspire, `--coverage`)
6. Contributors can discover/run tests with MTP enabled
