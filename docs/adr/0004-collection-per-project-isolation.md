# 0004. Collection-per-project isolation over payload multitenancy

- **Status:** Accepted
- **Date:** 2026-07-02
- **Deciders:** Maintainers
- **Related:** [Multitenancy with LlamaIndex](https://qdrant.tech/documentation/examples/llama-index-multitenancy/), [Qdrant multitenancy guide](https://qdrant.tech/documentation/manage-data/multitenancy/)

## Context

The MCP server mounts `/workspace` and treats **each direct subfolder as one indexed project**. Today that maps 1:1 to a **separate Qdrant collection** named after the folder basename (see [`ARCHITECTURE.md`](../ARCHITECTURE.md)).

Qdrant’s [Multitenancy with LlamaIndex](https://qdrant.tech/documentation/examples/llama-index-multitenancy/) guided sample and [multitenancy documentation](https://qdrant.tech/documentation/manage-data/multitenancy/) recommend a different pattern for many tenants:

- **One collection per embedding model** with payload-based partitioning (`group_id` / `library` metadata field)
- Payload index on the tenant field with `is_tenant=true`
- Optional HNSW tuning (`m=0`, `payload_m=16`) when all queries are tenant-scoped
- Tiered multitenancy (v1.16+) for dedicated shards per large tenant

Our deployment serves a **single operator** (or small team) indexing their own repos — not a SaaS serving unrelated external users. Still, we routinely search **one project at a time** or a **small explicit subset** via `collection` / `collections` tool parameters. Cross-collection search re-fuses ranked lists globally ([ADR 0003](0003-hybrid-search-rrf-default.md)).

We need a decision on tenant isolation strategy before collection count grows (dozens of repos) and before adopting Qdrant tiered multitenancy or shard routing.

## Decision

We will keep **one Qdrant collection per workspace project folder** as the default isolation model. Payload fields (`rel_path`, `language`, `symbol_name`, etc.) filter **within** a collection; the collection name scopes **between** projects.

We will **not** migrate to single-collection payload multitenancy unless operational metrics (collection count, Qdrant memory overhead, optimizer pressure) justify it.

### Rationale for collection-per-project

| Factor | Collection-per-project | Payload multitenancy |
|--------|------------------------|----------------------|
| Mental model | Folder name = collection name = MCP `collection` param | Requires `group_id` filter on every query |
| Index lifecycle | `index_codebase(path="foo")` touches one collection | Shared collection; delete/recreate affects all tenants |
| Model/backend change | Recreate one collection on mismatch | Full collection rebuild or complex migration |
| Cron reindex | One git repo → one collection | Must tag every point with tenant id |
| Cross-project search | Explicit multi-collection query + global RRF | Single query with `group_id IN [...]` filter |
| Qdrant overhead | N collections × HNSW graphs | One graph; payload-filtered search |

### Payload indexes (within collection)

We adopt the multitenancy tutorial’s **payload index** lesson without its single-collection layout: keyword indexes on `rel_path`, `chunk_id`, `symbol_name`, `language` when `PAYLOAD_INDEXES=true` (default). These accelerate outline, chunk lookup, and filtered search — the same performance principle as indexing `metadata.library` in the LlamaIndex example.

We do **not** apply multitenancy-specific HNSW config (`m=0`) because we occasionally run cross-collection global search and collection-wide scrolls (`get_collection_summary`).

### Future migration trigger

Revisit payload multitenancy when **any** of:

- \> ~50 active collections and measurable Qdrant baseline RAM growth
- Need for Qdrant tiered multitenancy / per-tenant dedicated shards
- Multi-user SaaS deployment with strict payload-level ACLs

Migration would add `collection` (tenant id) payload field, consolidate collections, and map MCP `collection` param to query filters — out of scope until triggered.

## Alternatives considered

| Option | Pros | Cons |
|--------|------|------|
| **Collection per project (chosen)** | Simple lifecycle; isolated re-index; matches workspace layout; no filter omission risk | Many collections; higher total HNSW overhead at scale |
| **Single collection + `group_id` payload** | Qdrant recommended at scale; one optimizer graph | Every query must filter; botched filter leaks cross-repo data; harder incremental deletes |
| **Tiered multitenancy (v1.16+)** | Dedicated shards for large tenants | Requires shard key routing; premature for local Docker single-node |
| **Separate Qdrant instance per project** | Hard isolation | Operational nightmare; defeats shared compose |
| **Collection per branch** | Fine-grained versioning | Explosion of collections; git already versions source |

## Consequences

### Positive

- `index_codebase`, cron reindex, and `delete_collection` map cleanly to one repo folder
- Backend/model mismatch recreates only the affected project
- MCP tool API stays intuitive: `collection="my-api"` equals folder name
- Payload indexes give within-project filter performance recommended by Qdrant multitenancy docs

### Negative / trade-offs

- Many small repos → many HNSW graphs; not optimal for Qdrant at hundreds of tenants
- Cross-collection search requires N parallel queries + global RRF (implemented in `fuse_cross_collection_rrf`)
- Does not use Qdrant `is_tenant` payload flag or tiered shard promotion

### Neutral / follow-ups

- Document collection count monitoring in [`DEPLOYMENT.md`](../DEPLOYMENT.md)
- Evaluate payload multitenancy ADR revision if SaaS multi-user mode is added
- Optional `group_id`-style field for monorepo subpackages deferred (use `rel_path` prefix filters instead)

## Implementation notes

### Affected paths

- `mcp_server/src/codebase_indexer/storage/qdrant.py` — `ensure_collection`, `_ensure_payload_indexes`
- `mcp_server/src/codebase_indexer/tools/index.py` — collection name = folder basename
- `mcp_server/src/codebase_indexer/tools/search_common.py` — multi-collection fan-out
- `cron/reindex.py` — one collection per git root

### Rollout

Default unchanged.

### Re-index

**No** for this decision alone. A future migration to payload multitenancy would require full re-index.

## Validation

- `test_index_status.py` — collection naming matches folder basename
- `test_qdrant_search.py` — multi-collection search respects per-collection isolation until explicitly merged
- Operational: monitor Qdrant collection count and RAM via `/collections` API in large workspaces
