# 0031. Split MCP liveness from dependency readiness

- **Status:** Accepted
- **Date:** 2026-07-21
- **Deciders:** Maintainers
- **Related:** [0018](0018-telemetry-observability-otel-prometheus.md) (metrics scrape), [0015](0015-colbert-http-sidecar.md) (ColBERT sidecar), [0025](0025-huggingface-tei-dense-embedding.md) (TEI dense), [0002](0002-graphrag-neo4j-qdrant.md) (Neo4j when graph on), [0030](0030-migrate-mcp-server-to-dotnet10.md) (.NET/Aspire production path), [DEPLOYMENT.md](../DEPLOYMENT.md)

## Context

Aspire Compose marks `mcp` healthy via an HTTP probe. Before this ADR, host `GET /health` was soft (process up) while TEI, remote ColBERT, or (when enabled) Neo4j could be unreachable — operators and automation then hit opaque embed/tool failures. Production runtime is .NET/Aspire ([ADR 0030](0030-migrate-mcp-server-to-dotnet10.md) Phase 7); probe URLs follow Aspire conventions (`/health` readiness, `/alive` liveness).

### Evaluation stack

| Layer | In scope? | Notes |
|-------|-----------|-------|
| Infrastructure correctness | yes | Dependency probes for required sidecars |
| Retrieval quality | no | Unchanged ranking paths |
| End-user MCP contract | partial | Ops probes split; MCP `get_health` tool remains process status |

## Decision

We **split liveness from readiness** on the .NET MCP HTTP surface (Aspire naming — **no** `/ready` route):

- `GET /health` = **readiness**: fails closed when required dense TEI is unreachable; when `Embedding:RerankEnabled=true` and remote ColBERT (`Colbert:EmbedBackend` empty or `remote`), sidecar `/health` must be OK; when `Graph:Enabled=true`, Neo4j bolt connectivity must succeed. Auth-exempt when bearer auth is enabled.
- `GET /alive` = **liveness** (always-on in all environments): process up; tags `live` only (`self` + `McpHostHealthCheck`); no dependency checks. Auth-exempt when bearer auth is enabled.
- Compose `mcp.healthcheck` and AppHost `WithHttpHealthCheck` target **readiness** (`/health`). `mcp` already `depends_on: tei: condition: service_healthy`, so healthcheck `start_period` covers MCP cold start (not TEI model download).

### In scope

- `TeiHealthCheck` / `Neo4jHealthCheck` / gated `ColbertRemoteHealthCheck` tagged `ready`
- Always-on `/alive`; retag process check to `live`
- Compose + AppHost probe wiring
- Unit tests (mocked backends) + Docker integration assertion
- DEPLOYMENT / `.env.example` / four-surface docs + CHANGELOG

### Out of scope

- Kubernetes manifests beyond documenting the two URLs
- Changing TEI/ColBERT/Neo4j images
- Phase 2 preload fail-closed / stricter cron gating
- Accept 0032/0033

### Default behavior and configuration

- *Default:* Compose/`mcp` healthy only when readiness deps pass; no new readiness feature flag
- *Configuration surface:* reuse `Tei__Url`, `Embedding__RerankEnabled`, `Colbert__EmbedBackend`/`Colbert__Url`, `Graph__Enabled`, `Graph__Neo4j*`; probe timeout hard-coded (~5s) — no `*_SCHEMA_VERSION`

### Phased delivery

1. Phase 1 — dependency-aware `/health` + always-on `/alive` + compose/AppHost probes + unit/integration tests (**this phase — Accepted**)
2. Phase 2 — Optional stricter gating / startup fail-closed when `Indexing:PreloadModels=true` and required deps down

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Chosen: Aspire `/health` readiness + `/alive` liveness** | Matches ServiceDefaults; compose/AppHost native | Operators must learn URL semantics |
| Soft `/health` only (status quo) | Zero code | Automation races broken deps |
| Python-style `/ready` + `/health` liveness | Familiar from early draft | Diverges from Aspire; dual naming |
| Fail process on preload error | Strong fail-fast | Harder local bring-up / TEI late start (Phase 2) |

## Consequences

### Positive

- Compose/AppHost do not mark MCP ready until TEI (and optional ColBERT/Neo4j) answer
- Clearer ops debugging (`/health` vs `/alive`)

### Negative / trade-offs

- Longer TEI `start_period` still needed on first model download (compose already waits for TEI healthy before starting `mcp`)
- Host TEI / Metal external TEI must be up before readiness passes

### Downstream work

- Phase 2 preload fail-closed
- Wire Prometheus scrape only after ready if desired ([0018](0018-telemetry-observability-otel-prometheus.md))

## Implementation notes

### New artifacts

- `src/CodebaseIndexer.Host/Health/TeiHealthCheck.cs`
- `src/CodebaseIndexer.Host/Health/Neo4jHealthCheck.cs`
- Host unit tests for dependency matrix

### Modified artifacts

- `HostApplicationBuilderExtensions` / `EndpointRouteBuilderExtensions`
- `Infrastructure` Neo4j `IDriver` DI sharing
- `docker-compose.aspire.yml` `mcp.healthcheck` → `/health`
- `AppHost.cs` `.WithHttpHealthCheck("/health")`
- `scripts/run_compose_integration.py` alive + TEI-down split
- Operator docs + CHANGELOG

### Rollout

- Default on merge (pre-release; no dual stack)

### Data migration

- none — re-index after pull unchanged

## Validation

### Automated tests

- *Unit* — mocked TEI/ColBERT/Neo4j success and failure paths; `/alive` 200 either way
- *Integration* — live Aspire compose: `/health` + `/alive` 200 when stack healthy; forced TEI stop → `/health` non-200 while `/alive` 200

### Success criteria

1. With full Aspire stack up, `/health` and `/alive` return 200 and Compose marks `mcp` healthy
2. With TEI stopped, `/health` fails and Compose does not report healthy after retries; `/alive` remains 200
3. `/alive` remains 200 without auth whenever the process is up
