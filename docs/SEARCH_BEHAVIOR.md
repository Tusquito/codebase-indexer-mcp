# Search Behavior

**Summary:** `search_codebase` caps `top_k` at **20**; `search_symbols` caps `top_k` at **30**. When `HYBRID_SEARCH` is enabled (default), results are ranked by reciprocal rank fusion (RRF) of dense and sparse lists — `min_score` is ignored because RRF scores are not on the cosine [0,1] scale. When `HYBRID_SEARCH` is disabled, only dense cosine search runs and `min_score` filters results by similarity threshold.

## `search_codebase`

| Parameter | Default | Cap / behavior |
|-----------|---------|----------------|
| `top_k` | `5` | Silently capped at **20** (`tools/search.py`) |
| `min_score` | `0.5` | Applied only when `HYBRID_SEARCH=false` |
| `max_content_chars` | `None` | Truncates chunk `content` in results; use `get_chunk` for full text |
| `language` | `None` | Optional Qdrant payload filter |
| `collection` / `collections` | default collection | Multi-collection search merges via global RRF re-fusion (rank-based, not raw score) |

Implementation path: `tools/search.py` → `tools/search_common.run_search` → `storage/qdrant.py` `QdrantStorage.search` → `_search_single`.

### Hybrid mode (`HYBRID_SEARCH=true`)

1. Query embedded to dense + sparse vectors
2. Qdrant prefetches `top_k * prefetch_multiplier` candidates on each channel (default multiplier **5**)
3. Dense prefetch applies `hnsw_ef` and, when `QUANTIZATION=true`, int8 rescoring with `quant_oversampling`
4. `Fusion.RRF` merges ranked lists within the collection
5. Multi-collection queries re-fuse per-collection ranked lists with global RRF (`rrf_k`, default **60**)
6. `score_threshold` forced to `0.0` — see `qdrant.py` `_search_single` comment on RRF vs cosine scales

### Dense-only mode (`HYBRID_SEARCH=false`)

- Single dense ANN query with cosine distance
- Query uses `hnsw_ef`; when `QUANTIZATION=true`, int8 rescoring with `quant_oversampling` is applied
- Results below `min_score` are dropped

## `search_symbols`

Same search backend as `search_codebase` but returns metadata only (no `content` field).

| Parameter | Default | Cap / behavior |
|-----------|---------|----------------|
| `top_k` | `10` | Silently capped at **30** (`tools/symbols.py`) |
| `min_score` | `0.4` | Applied only when `HYBRID_SEARCH=false` |

## Related tools

| Tool | Embedding cost | Notes |
|------|----------------|-------|
| `get_collection_summary` | Zero | Payload scroll only |
| `get_file_outline` | Zero | Payload scroll by `rel_path` |
| `get_chunk` | Zero | Lookup by `chunk_id` |
| `find_cross_references` | Per internal search | Uses fixed internal `min_score` values |
| `map_service_dependencies` | Batched query embed | Uses `min_score=0.25` internally |

## Configuration

| Variable | Default | Effect |
|----------|---------|--------|
| `HYBRID_SEARCH` | `true` | Enables sparse channel + RRF fusion |
| `DENSE_EMBED_MODEL` | *(required)* | Dense query embedding model |
| `SPARSE_EMBED_MODEL` | *(required)* | Sparse query embedding model |
| `PREFETCH_MULTIPLIER` | `5` | Hybrid prefetch limit = `top_k * multiplier` per channel |
| `QUANT_OVERSAMPLING` | `2.0` | Quantized dense search oversampling (when `QUANTIZATION=true`) |
| `HNSW_EF` | `64` | Query-time HNSW search breadth |
| `RRF_K` | `60` | RRF constant for multi-collection re-fusion |

Disabling hybrid requires re-creating collections whose sparse configuration no longer matches — `QdrantStorage.ensure_collection` detects hybrid mismatch and recreates when needed.

## Optional ColBERT reranking (`RERANK_ENABLED=true`)

When enabled (default **off**), search runs a three-stage pipeline per collection:

1. Hybrid prefetch on dense + sparse channels (`RERANK_PREFETCH` candidates each, default **100**)
2. ColBERT **MAX_SIM** rerank over the merged candidate pool (`using=colbert`)
3. Multi-collection queries re-fuse per-collection ranked lists with global RRF (`rrf_k`)

Index-time: a third multivector field `colbert` is stored on each point (HNSW disabled, rerank-only). Enabling rerank on an existing collection **requires a full re-index** — `ensure_collection` recreates when the colbert vector config is missing or mismatched.

| Variable | Default | Effect |
|----------|---------|--------|
| `RERANK_ENABLED` | `false` | Master switch (requires `HYBRID_SEARCH=true`) |
| `COLBERT_EMBED_MODEL` | `colbert-ir/colbertv2.0` | fastembed ColBERT model for index + query |
| `RERANK_PREFETCH` | `100` | Hybrid candidate pool before ColBERT rerank |
| `RERANK_MAX_QUERY_TOKENS` | `0` | Query truncation; `0` = registry default |

`min_score` remains disabled on hybrid and rerank paths (scores are not cosine-scale).

## Multi-hop retrieval

Many code questions need evidence from **more than one chunk or file**. The server does **not** run an in-server decomposition loop ([decision 0009](adr/0009-multi-hop-retrieval-strategies.md)); the **MCP client** orchestrates hops. Reference: [Qdrant query decomposition](https://qdrant.tech/documentation/improve-search/query-decomposition/).

### Choose a strategy

```mermaid
flowchart TD
    Q[Multi-hop question]
    Q --> Struct{Structural edge?}
    Struct -->|imports calls HTTP endpoints| Chain[Tool chain]
    Struct -->|prose config narrative| Decomp[Client decomposition]
    Struct -->|graph enabled| Graph[expand_search_context proposed]
    Chain --> Xref[find_cross_references]
    Chain --> Map[map_service_dependencies]
    Chain --> Sym[search_symbols then get_chunk]
    Decomp --> S1[search_codebase hop 1]
    S1 --> Sub[Client sub-question]
    Sub --> S2[search_codebase hop 2+]
    S2 --> Fuse[Fuse all hops RRF]
    Xref --> Fuse
    Map --> Fuse
    Sym --> Fuse
    Graph --> Fuse
```

| Strategy | When to use | Typical tools | Embedding cost |
|----------|-------------|---------------|----------------|
| **Tool chaining** | Known relation types (symbol, xref, service map) | `search_symbols` → `get_chunk`; `find_cross_references`; `map_service_dependencies` | 0–1 embed per hop |
| **Query decomposition** | Facts not linked in graph (config prose, comments, docs) | Repeated `search_codebase` with client-drafted sub-questions | 1 embed per `search_codebase` hop |
| **Graph expansion** | Call/import/HTTP paths when graph layer enabled ([0002](adr/0002-graphrag-neo4j-qdrant.md)) | `expand_search_context` (proposed) | One search + graph query |

Reranking or a wider single-pass `top_k` **cannot recover evidence that was never retrieved** — add hops when bridging chunks are missing from hop 1.

### Client decomposition loop

1. `search_codebase` with the user question (`top_k` 10–20).
2. Client LLM reads returned chunks (use `max_content_chars` + `get_chunk` for full text).
3. If evidence is incomplete, emit a **sub-question** targeting the missing hop (or `DONE`).
4. `search_codebase` again (or `search_symbols` / `find_cross_references` when the hop is structural).
5. Repeat until `DONE` or a hop budget (typically 2–4 searches).
6. **Fuse every hop** before synthesis — not only the last result list.

### Client-side RRF merge (chunk_ids)

Use rank-based RRF on `chunk_id` across hops (same idea as server `rrf_k`, default 60):

```
fused[chunk_id] += 1 / (rrf_k + rank_in_hop)
```

Keep the best rank per `chunk_id` per hop, sort fused scores descending, then `get_chunk` the top entries for the answer step. Stable `chunk_id` keys are `sha256("{rel_path}:{start_line}")` in payloads.

### Token-efficient hops

| Step | Tool | Embed cost |
|------|------|------------|
| Locate symbol | `search_symbols` | yes |
| File structure | `get_file_outline` | no |
| Full chunk text | `get_chunk` | no |
| Cross-project edge | `find_cross_references` | internal search |
| Service graph | `map_service_dependencies` | batched embed |

### Evaluation

Multi-hop queries in `mcp_server/benchmarks/fixtures/golden_queries.jsonl` are tagged `multi_hop`. Single-pass `search_codebase` often scores lower on those queries by design; compare against a 2-hop client script using [eval_retrieval](ARCHITECTURE.md#retrieval-evaluation-adr-0007).
