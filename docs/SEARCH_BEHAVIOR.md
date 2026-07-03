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
| `find_cross_references` | Per internal search | Participates in ColBERT rerank when `RERANK_ENABLED=true`; internal `min_score=0.3` ignored on hybrid/rerank paths |
| `map_service_dependencies` | Batched query embed | Participates in ColBERT rerank when `RERANK_ENABLED=true`; internal `min_score=0.25` ignored on hybrid/rerank paths |
| `recommend_code` | Per positive/negative text query | Dense-only Qdrant Recommendation API; single collection; see below |

## `recommend_code`

Find chunks **similar to positive examples** and **dissimilar from negative examples** using Qdrant's Recommendation API on the **dense** vector only (`RecommendStrategy.AVERAGE_VECTOR`).

| Parameter | Default | Cap / behavior |
|-----------|---------|----------------|
| `collection` | *(required)* | Single collection only — multi-collection deferred |
| `positive_chunk_ids` | `None` | Resolved to point IDs; missing IDs fail fast with explicit error |
| `positive_query` | `None` | Free-text embedded via Ollama dense path |
| `negative_chunk_ids` | `None` | Same resolution/validation as positives |
| `negative_query` | `None` | Free-text embedded via Ollama dense path |
| `limit` | `5` | Silently capped at **20** |
| `language` | `None` | Qdrant payload filter (indexed field) |
| `path_glob` | `None` | Post-filter via `fnmatch` on `rel_path`; over-fetches `limit * 3` |
| `max_content_chars` | `None` | Truncates chunk `content`; use `get_chunk` for full text |

At least one positive example (`positive_chunk_ids` and/or `positive_query`) is required. Total example count (positive + negative, chunk IDs + text queries) is capped by `RECOMMEND_MAX_EXAMPLES` (default **10**).

Implementation path: `tools/recommend.py` → `storage/qdrant.py` `QdrantStorage.recommend` → `query_points` with `RecommendQuery` on `using=dense`.

| Variable | Default | Effect |
|----------|---------|--------|
| `RECOMMEND_ENABLED` | `true` | Master switch; when `false`, tool is not registered |
| `RECOMMEND_MAX_EXAMPLES` | `10` | Cap on positive + negative examples per request |

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
| `COLBERT_EMBED_BACKEND` | `onnx` | `onnx` (in MCP) or `remote` (HTTP sidecar) |
| `COLBERT_URL` | `http://colbert_worker:8082` | Sidecar base URL when `remote` |
| `COLBERT_TIMEOUT` | `300` | Per-request HTTP timeout (seconds) |
| `COLBERT_EMBED_BATCH_SIZE` | `16` | MCP → sidecar batch size |

When `COLBERT_EMBED_BACKEND=remote`, ColBERT model weights and inference run in the `colbert_worker` container (see `docker-compose.colbert-worker.yml`). MCP still holds returned multivectors per flush batch until upsert — the sidecar removes ColBERT **model and compute** RAM from MCP, not the upsert payload. Switching `onnx` ↔ `remote` with the same `COLBERT_EMBED_MODEL` does **not** require re-index.

### Index-time tuning (upsert batch size)

ColBERT multivectors make each Qdrant point much larger than dense+sparse alone. If `UPSERT_BATCH` is too high, upserts fail with connection errors (often logged as empty `Upsert error:` strings). Lower **`UPSERT_BATCH`** to **`10`–`25`** when rerank is enabled; see [DEPLOYMENT.md](DEPLOYMENT.md#colbert-rerank-qdrant-upsert-batching) for symptoms, cause, and presets.

| Variable | Default (no rerank) | With rerank |
|----------|---------------------|-------------|
| `UPSERT_BATCH` | `500` | **`10`–`25`** recommended |
| `FLUSH_EVERY` | `1500` | **`64`–`128`** typical |

`min_score` remains disabled on hybrid and rerank paths (scores are not cosine-scale). This applies to `search_codebase`, `search_symbols`, `find_cross_references`, and `map_service_dependencies`.

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
