# Search Behavior

**Summary:** `search_codebase` caps `top_k` at **20**; `search_symbols` caps `top_k` at **30**. When `HYBRID_SEARCH` is enabled (default), results are ranked by reciprocal rank fusion (RRF) of dense and sparse lists — `min_score` is ignored because RRF scores are not on the cosine [0,1] scale. When `HYBRID_SEARCH` is disabled, only dense cosine search runs and `min_score` filters results by similarity threshold.

## `search_codebase`

| Parameter | Default | Cap / behavior |
|-----------|---------|----------------|
| `top_k` | `5` | Silently capped at **20** (`tools/search.py`) |
| `min_score` | `0.5` | Applied only when `HYBRID_SEARCH=false` |
| `max_content_chars` | `None` | Truncates chunk `content` in results; use `get_chunk` for full text |
| `language` | `None` | Optional Qdrant payload filter |
| `collection` / `collections` | default collection | Multi-collection search merges and re-ranks by score |

Implementation path: `tools/search.py` → `tools/search_common.run_search` → `storage/qdrant.py` `QdrantStorage.search` → `_search_single`.

### Hybrid mode (`HYBRID_SEARCH=true`)

1. Query embedded to dense + sparse vectors
2. Qdrant prefetches `top_k * 3` candidates on each channel
3. `Fusion.RRF` merges ranked lists
4. `score_threshold` forced to `0.0` — see `qdrant.py` `_search_single` comment on RRF vs cosine scales

### Dense-only mode (`HYBRID_SEARCH=false`)

- Single dense ANN query with cosine distance
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

Disabling hybrid requires re-creating collections whose sparse configuration no longer matches — `QdrantStorage.ensure_collection` detects hybrid mismatch and recreates when needed.
