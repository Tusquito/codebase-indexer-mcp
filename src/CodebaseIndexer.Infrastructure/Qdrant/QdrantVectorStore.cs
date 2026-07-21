using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Embedding;
using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using DomainSparseVector = CodebaseIndexer.Domain.Models.SparseVector;
using GrpcMatch = Qdrant.Client.Grpc.Match;

namespace CodebaseIndexer.Infrastructure.Qdrant;

/// <summary>Qdrant-backed implementation of <see cref="IVectorStore"/>.</summary>
public sealed class QdrantVectorStore : IVectorStore
{
    private static readonly Guid ChunkNamespace = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    private const string GraphCallSitesMetadataKey = "graph_call_sites";
    private const string GraphEnabledMetadataKey = "graph_enabled";

    private static readonly string[] IndexedPayloadFields =
        ["rel_path", "chunk_id", "symbol_name", "language", "callees"];

    private readonly QdrantOptions _qdrant;
    private readonly EmbeddingOptions _embedding;
    private readonly ColbertOptions _colbert;
    private readonly DiscoveryOptions _discovery;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly Lazy<QdrantClient> _client;
    private readonly AdaptiveRerankStats _adaptiveStats = new();

    /// <summary>Creates a vector store client from Qdrant and embedding options.</summary>
    public QdrantVectorStore(
        IOptions<QdrantOptions> qdrant,
        IOptions<EmbeddingOptions> embedding,
        IOptions<ColbertOptions> colbert,
        IOptions<DiscoveryOptions> discovery,
        ILogger<QdrantVectorStore> logger)
    {
        _qdrant = qdrant.Value;
        _embedding = embedding.Value;
        _colbert = colbert.Value;
        _discovery = discovery.Value;
        _logger = logger;
        _client = new Lazy<QdrantClient>(() => CreateClient(_qdrant));
    }

    /// <summary>Adaptive ColBERT skip/rerank counters (test surface).</summary>
    public AdaptiveRerankStats AdaptiveRerankStats => _adaptiveStats;

    /// <summary>Resets adaptive ColBERT counters.</summary>
    public void ResetAdaptiveStats() => _adaptiveStats.Reset();

    private int ColbertTokenSize =>
        KnownColbertModels.ResolveTokenDimension(
            string.IsNullOrWhiteSpace(_colbert.EmbedModel)
                ? _embedding.ColbertEmbedModel
                : _colbert.EmbedModel);

    /// <inheritdoc />
    public async ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default)
    {
        var collections = await _client.Value.ListCollectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return collections.Contains(collection);
    }

    /// <inheritdoc />
    public async Task EnsureCollectionAsync(string collection, bool force = false, CancellationToken cancellationToken = default)
    {
        if (await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            if (!force)
            {
                var info = await _client.Value.GetCollectionInfoAsync(collection, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!NeedsRecreate(info))
                {
                    if (_qdrant.PayloadIndexes)
                    {
                        await EnsurePayloadIndexesAsync(collection, cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }
            }

            await _client.Value.DeleteCollectionAsync(collection, cancellationToken: cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "Recreated Qdrant collection {Collection}. Re-index after pull (no schema-version env).",
                collection);
        }

        var vectorsConfig = new VectorParamsMap
        {
            Map =
            {
                ["dense"] = new VectorParams
                {
                    Size = (ulong)_embedding.DenseVectorSize,
                    Distance = Distance.Cosine,
                    OnDisk = _qdrant.VectorsOnDisk,
                },
            },
        };

        SparseVectorConfig? sparseVectorsConfig = null;
        if (_embedding.HybridSearch)
        {
            sparseVectorsConfig = new SparseVectorConfig
            {
                Map =
                {
                    ["sparse"] = new SparseVectorParams
                    {
                        Index = new SparseIndexConfig { OnDisk = _qdrant.SparseOnDisk },
                    },
                },
            };
        }

        if (_embedding.RerankEnabled)
        {
            vectorsConfig.Map["colbert"] = new VectorParams
            {
                Size = (ulong)ColbertTokenSize,
                Distance = Distance.Cosine,
                MultivectorConfig = new MultiVectorConfig
                {
                    Comparator = MultiVectorComparator.MaxSim,
                },
                HnswConfig = new HnswConfigDiff { M = 0 },
                OnDisk = _qdrant.VectorsOnDisk,
            };
        }

        QuantizationConfig? quantizationConfig = null;
        if (_qdrant.Quantization)
        {
            quantizationConfig = new QuantizationConfig
            {
                Scalar = new ScalarQuantization
                {
                    Type = QuantizationType.Int8,
                    AlwaysRam = true,
                },
            };
        }

        await _client.Value.CreateCollectionAsync(
            collection,
            vectorsConfig: vectorsConfig,
            sparseVectorsConfig: sparseVectorsConfig,
            quantizationConfig: quantizationConfig,
            hnswConfig: new HnswConfigDiff
            {
                M = (ulong)_qdrant.HnswM,
                EfConstruct = (ulong)_qdrant.HnswEfConstruct,
            },
            optimizersConfig: new OptimizersConfigDiff
            {
                MemmapThreshold = (ulong)_qdrant.MemmapThresholdKb,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (_qdrant.PayloadIndexes)
        {
            await EnsurePayloadIndexesAsync(collection, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task UpsertChunksAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        bool omitCallees = false,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? graphNodeIdsByChunk = null,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var points = chunks
            .Select(c => ToPoint(
                c,
                omitCallees,
                graphNodeIdsByChunk is not null
                && graphNodeIdsByChunk.TryGetValue(c.Chunk.Id.Value, out var ids)
                    ? ids
                    : null))
            .ToList();
        await _client.Value.UpsertAsync(collection, points, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetCollectionGraphCallSitesAsync(
        string collection,
        bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        await SetCollectionMetadataFlagAsync(collection, GraphCallSitesMetadataKey, enabled, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "collection_graph_call_sites_set collection={Collection} enabled={Enabled}",
            collection,
            enabled);
    }

    /// <inheritdoc />
    public async Task SetCollectionGraphEnabledAsync(
        string collection,
        bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        await SetCollectionMetadataFlagAsync(collection, GraphEnabledMetadataKey, enabled, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "collection_graph_enabled_set collection={Collection} enabled={Enabled}",
            collection,
            enabled);
    }

    /// <inheritdoc />
    public async ValueTask<bool> CollectionHasGraphCallSitesAsync(
        string collection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadCollectionMetadataFlagAsync(collection, GraphCallSitesMetadataKey, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "collection_graph_call_sites_read_error collection={Collection}", collection);
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> CollectionHasGraphEnabledAsync(
        string collection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadCollectionMetadataFlagAsync(collection, GraphEnabledMetadataKey, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "collection_graph_enabled_read_error collection={Collection}", collection);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string collection,
        IReadOnlyList<float> denseVector,
        DomainSparseVector? sparseVector,
        int topK,
        string? language = null,
        float minScore = 0.5f,
        IReadOnlyList<IReadOnlyList<float>>? colbertVector = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return Array.Empty<SearchHit>();
        }

        var queryFilter = BuildLanguageFilter(language);
        var denseParams = BuildDenseSearchParams();
        var usedHybrid = sparseVector is not null && _embedding.HybridSearch;
        var usedRerank = _embedding.RerankEnabled && colbertVector is not null && usedHybrid;
        var usedAdaptive = usedRerank && _embedding.RerankAdaptiveEnabled;
        var prefetchLimit = ResolveSearchPrefetchLimit(
            usedRerank,
            _embedding.RerankPrefetch,
            topK,
            _embedding.PrefetchMultiplier);
        var scoreThreshold = usedHybrid || usedRerank ? 0f : minScore;
        var denseArray = denseVector as float[] ?? denseVector.ToArray();

        if (usedAdaptive)
        {
            var probeLimit = Math.Max(topK, 2);
            var probe = await HybridRrfQueryAsync(
                collection,
                denseArray,
                sparseVector!,
                probeLimit,
                prefetchLimit,
                queryFilter,
                denseParams,
                cancellationToken).ConfigureAwait(false);
            _adaptiveStats.Total++;
            if (ShouldSkipColbertAfterProbe(probe, _embedding.RerankAdaptiveGap))
            {
                var gap = probe[0].Score - probe[1].Score;
                _adaptiveStats.Skipped++;
                _logger.LogDebug(
                    "adaptive_rerank_skip collection={Collection} gap={Gap} threshold={Threshold}",
                    collection,
                    gap,
                    _embedding.RerankAdaptiveGap);
                return MapPointsToHits(probe.Take(topK).ToArray(), collection, scoreThreshold);
            }

            _adaptiveStats.Reranked++;
        }

        IReadOnlyList<ScoredPoint> points;
        if (usedRerank)
        {
            var sparseIndices = sparseVector!.Indices.Select(i => (uint)i).ToArray();
            var sparseValues = sparseVector.Values as float[] ?? sparseVector.Values.ToArray();
            var colbertQuery = ToMultiVector(colbertVector!);
            points = await _client.Value.QueryAsync(
                collection,
                query: colbertQuery,
                usingVector: "colbert",
                prefetch:
                [
                    new PrefetchQuery
                    {
                        Query = denseArray,
                        Using = "dense",
                        Limit = prefetchLimit,
                        Params = denseParams,
                        Filter = queryFilter,
                    },
                    new PrefetchQuery
                    {
                        Query = (sparseValues, sparseIndices),
                        Using = "sparse",
                        Limit = prefetchLimit,
                        Filter = queryFilter,
                    },
                ],
                filter: queryFilter,
                limit: (ulong)topK,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: new WithVectorsSelector { Enable = false },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else if (usedHybrid)
        {
            points = await HybridRrfQueryAsync(
                collection,
                denseArray,
                sparseVector!,
                topK,
                prefetchLimit,
                queryFilter,
                denseParams,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            points = await _client.Value.QueryAsync(
                collection,
                query: denseArray,
                usingVector: "dense",
                filter: queryFilter,
                searchParams: denseParams,
                limit: (ulong)topK,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: new WithVectorsSelector { Enable = false },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return MapPointsToHits(points, collection, scoreThreshold);
    }

    private async Task<IReadOnlyList<ScoredPoint>> HybridRrfQueryAsync(
        string collection,
        float[] denseArray,
        DomainSparseVector sparseVector,
        int limit,
        ulong prefetchLimit,
        Filter? queryFilter,
        SearchParams denseParams,
        CancellationToken cancellationToken)
    {
        var sparseIndices = sparseVector.Indices.Select(i => (uint)i).ToArray();
        var sparseValues = sparseVector.Values as float[] ?? sparseVector.Values.ToArray();
        return await _client.Value.QueryAsync(
            collection,
            query: Fusion.Rrf,
            prefetch:
            [
                new PrefetchQuery
                {
                    Query = denseArray,
                    Using = "dense",
                    Limit = prefetchLimit,
                    Params = denseParams,
                    Filter = queryFilter,
                },
                new PrefetchQuery
                {
                    Query = (sparseValues, sparseIndices),
                    Using = "sparse",
                    Limit = prefetchLimit,
                    Filter = queryFilter,
                },
            ],
            filter: queryFilter,
            limit: (ulong)limit,
            payloadSelector: new WithPayloadSelector { Enable = true },
            vectorsSelector: new WithVectorsSelector { Enable = false },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static float[][] ToMultiVector(IReadOnlyList<IReadOnlyList<float>> colbertVector)
    {
        var jagged = new float[colbertVector.Count][];
        for (var i = 0; i < colbertVector.Count; i++)
        {
            jagged[i] = colbertVector[i] as float[] ?? colbertVector[i].ToArray();
        }

        return jagged;
    }

    /// <inheritdoc />
    public async Task<ChunkPayload?> GetChunkByIdAsync(
        string collection,
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var result = await _client.Value.ScrollAsync(
            collection,
            filter: new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "chunk_id",
                            Match = new GrpcMatch { Keyword = chunkId },
                        },
                    },
                },
            },
            limit: 1,
            payloadSelector: new WithPayloadSelector { Enable = true },
            vectorsSelector: new WithVectorsSelector { Enable = false },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var point = result.Result.FirstOrDefault();
        return point is null ? null : MapPayload(point.Payload, collection);
    }

    /// <inheritdoc />
    public async Task<ChunkPayload?> FindChunkByIdAsync(
        string chunkId,
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(collection))
        {
            return await GetChunkByIdAsync(collection, chunkId, cancellationToken).ConfigureAwait(false);
        }

        var stats = await ListCollectionStatsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var coll in stats)
        {
            var found = await GetChunkByIdAsync(coll.Name, chunkId, cancellationToken).ConfigureAwait(false);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileSymbol>> ScrollFileSymbolsAsync(
        string collection,
        string relPath,
        CancellationToken cancellationToken = default)
    {
        var symbols = new List<FileSymbol>();
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return symbols;
        }

        try
        {
            PointId? offset = null;
            while (true)
            {
                var result = await _client.Value.ScrollAsync(
                    collection,
                    filter: new Filter
                    {
                        Must =
                        {
                            new Condition
                            {
                                Field = new FieldCondition
                                {
                                    Key = "rel_path",
                                    Match = new GrpcMatch { Keyword = relPath },
                                },
                            },
                        },
                    },
                    limit: 1000,
                    offset: offset,
                    payloadSelector: new WithPayloadSelector
                    {
                        Include = new PayloadIncludeSelector
                        {
                            Fields = { "chunk_id", "symbol_name", "symbol_type", "start_line", "end_line", "language" },
                        },
                    },
                    vectorsSelector: new WithVectorsSelector { Enable = false },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                foreach (var point in result.Result)
                {
                    var p = point.Payload;
                    symbols.Add(new FileSymbol(
                        GetString(p, "chunk_id"),
                        GetOptionalString(p, "symbol_name"),
                        GetString(p, "symbol_type", "other"),
                        GetInt(p, "start_line"),
                        GetInt(p, "end_line"),
                        GetString(p, "language")));
                }

                if (result.NextPageOffset is null)
                {
                    break;
                }

                offset = result.NextPageOffset;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "scroll_file_symbols_error collection={Collection} path={Path}", collection, relPath);
        }

        return symbols.OrderBy(s => s.StartLine).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PayloadRow>> ScrollAllPayloadsAsync(
        string collection,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<PayloadRow>();
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return rows;
        }

        try
        {
            PointId? offset = null;
            while (true)
            {
                var result = await _client.Value.ScrollAsync(
                    collection,
                    limit: 10_000,
                    offset: offset,
                    payloadSelector: new WithPayloadSelector
                    {
                        Include = new PayloadIncludeSelector
                        {
                            Fields = { "rel_path", "language", "symbol_name", "symbol_type", "start_line", "end_line" },
                        },
                    },
                    vectorsSelector: new WithVectorsSelector { Enable = false },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                foreach (var point in result.Result)
                {
                    var p = point.Payload;
                    rows.Add(new PayloadRow(
                        GetString(p, "rel_path"),
                        GetString(p, "language"),
                        GetOptionalString(p, "symbol_name"),
                        GetString(p, "symbol_type", "other"),
                        GetInt(p, "start_line"),
                        GetInt(p, "end_line")));
                }

                if (result.NextPageOffset is null)
                {
                    break;
                }

                offset = result.NextPageOffset;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "scroll_all_payloads_error collection={Collection}", collection);
        }

        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionStats>> ListCollectionStatsAsync(
        CancellationToken cancellationToken = default)
    {
        var names = await ListCollectionsAsync(cancellationToken).ConfigureAwait(false);
        var stats = new List<CollectionStats>(names.Count);
        foreach (var name in names)
        {
            var s = await GetCollectionStatsAsync(name, cancellationToken).ConfigureAwait(false);
            if (s is not null)
            {
                stats.Add(s);
            }
        }

        return stats;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHit>> FindSymbolInCollectionsAsync(
        string symbolName,
        IReadOnlyList<string> collections,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default)
    {
        var tasks = collections.Select(async coll =>
        {
            try
            {
                if (!await CollectionExistsAsync(coll, cancellationToken).ConfigureAwait(false))
                {
                    return (IReadOnlyList<SearchHit>)Array.Empty<SearchHit>();
                }

                var result = await _client.Value.ScrollAsync(
                    coll,
                    filter: new Filter
                    {
                        Must =
                        {
                            new Condition
                            {
                                Field = new FieldCondition
                                {
                                    Key = "symbol_name",
                                    Match = new GrpcMatch { Keyword = symbolName },
                                },
                            },
                        },
                    },
                    limit: (uint)limitPerCollection,
                    payloadSelector: new WithPayloadSelector { Enable = true },
                    vectorsSelector: new WithVectorsSelector { Enable = false },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                return result.Result.Select(p =>
                {
                    var payload = p.Payload;
                    var id = GetString(payload, "chunk_id");
                    return new SearchHit(
                        new ChunkId(id),
                        0,
                        GetString(payload, "rel_path"),
                        GetString(payload, "language"),
                        GetInt(payload, "start_line"),
                        GetInt(payload, "end_line"),
                        GetOptionalString(payload, "symbol_name"),
                        GetString(payload, "symbol_type", "other"),
                        GetString(payload, "content"),
                        coll);
                }).ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "symbol_scroll_error collection={Collection}", coll);
                return (IReadOnlyList<SearchHit>)Array.Empty<SearchHit>();
            }
        });

        var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
        return batches.SelectMany(b => b).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var collections = await _client.Value.ListCollectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return collections.OrderBy(c => c, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<CollectionStats?> GetCollectionStatsAsync(
        string collection,
        CancellationToken cancellationToken = default)
    {
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var info = await _client.Value.GetCollectionInfoAsync(collection, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var graphCallSites = MetadataFlagTrue(info.Config?.Metadata, GraphCallSitesMetadataKey);
        var graphEnabled = MetadataFlagTrue(info.Config?.Metadata, GraphEnabledMetadataKey);

        return new CollectionStats(
            collection,
            (long)info.PointsCount,
            0,
            _embedding.DenseModel,
            _embedding.SparseModel,
            EmbedderBackendKeys.Dense.Tei,
            _embedding.HybridSearch,
            _embedding.RerankEnabled,
            GraphCallSites: graphCallSites,
            GraphEnabled: graphEnabled);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, FileMetadata>> GetFileMetadataAsync(
        string collection,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, FileMetadata>(StringComparer.Ordinal);
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return metadata;
        }

        PointId? offset = null;
        while (true)
        {
            var result = await _client.Value.ScrollAsync(
                collection,
                limit: 10_000,
                offset: offset,
                payloadSelector: new WithPayloadSelector
                {
                    Include = new PayloadIncludeSelector
                    {
                        Fields = { "rel_path", "file_sha256", "file_mtime" },
                    },
                },
                vectorsSelector: new WithVectorsSelector { Enable = false },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var point in result.Result)
            {
                if (!point.Payload.TryGetValue("rel_path", out var relPathValue)
                    || !point.Payload.TryGetValue("file_sha256", out var hashValue))
                {
                    continue;
                }

                var relPath = relPathValue.StringValue;
                var hash = hashValue.StringValue;
                if (string.IsNullOrEmpty(relPath) || string.IsNullOrEmpty(hash) || metadata.ContainsKey(relPath))
                {
                    continue;
                }

                double? mtime = null;
                if (point.Payload.TryGetValue("file_mtime", out var mtimeValue)
                    && mtimeValue.KindCase == Value.KindOneofCase.DoubleValue)
                {
                    mtime = mtimeValue.DoubleValue;
                }

                metadata[relPath] = new FileMetadata(hash, mtime);
            }

            if (result.NextPageOffset is null)
            {
                break;
            }

            offset = result.NextPageOffset;
        }

        return metadata;
    }

    /// <inheritdoc />
    public async Task DeleteByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default)
    {
        if (relPaths.Count == 0)
        {
            return;
        }

        const int batchSize = 100;
        for (var offset = 0; offset < relPaths.Count; offset += batchSize)
        {
            var batch = relPaths.Skip(offset).Take(batchSize).ToArray();
            var filter = new Filter
            {
                Should =
                {
                    batch.Select(path => new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "rel_path",
                            Match = new GrpcMatch { Keyword = path },
                        },
                    }),
                },
            };

            await _client.Value.DeleteAsync(collection, filter, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task SetIndexingAsync(
        string collection,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var threshold = enabled ? 20_000ul : 0ul;
        try
        {
            await _client.Value.UpdateCollectionAsync(
                collection,
                optimizersConfig: new OptimizersConfigDiff { IndexingThreshold = threshold },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "set_indexing_error collection={Collection} enabled={Enabled}", collection, enabled);
        }
    }

    /// <inheritdoc />
    public async Task VerifyChunkIdsExistAsync(
        string collection,
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        if (chunkIds.Count == 0)
        {
            return;
        }

        var pointIds = chunkIds
            .Select(id => new PointId { Uuid = ChunkIdToPointUuid(id) })
            .ToArray();
        var records = await _client.Value.RetrieveAsync(
            collection,
            pointIds,
            withPayload: false,
            withVectors: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var found = records.Select(r => r.Id.Uuid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = chunkIds
            .Where((cid, i) => !found.Contains(pointIds[i].Uuid))
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException(
                $"Unknown chunk_id(s) in collection '{collection}': {string.Join(", ", unknown)}");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHit>> RecommendAsync(
        string collection,
        IReadOnlyList<RecommendExample> positive,
        IReadOnlyList<RecommendExample>? negative = null,
        int limit = 5,
        string? language = null,
        string? pathGlob = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return Array.Empty<SearchHit>();
        }

        var input = new RecommendInput { Strategy = RecommendStrategy.AverageVector };
        AddRecommendExamples(input.Positive, positive);
        if (negative is { Count: > 0 })
        {
            AddRecommendExamples(input.Negative, negative);
        }

        var fetchLimit = string.IsNullOrEmpty(pathGlob) ? limit : limit * 3;
        var points = await _client.Value.QueryAsync(
            collection,
            query: input,
            usingVector: "dense",
            filter: BuildLanguageFilter(language),
            searchParams: BuildDenseSearchParams(),
            limit: (ulong)fetchLimit,
            payloadSelector: new WithPayloadSelector { Enable = true },
            vectorsSelector: new WithVectorsSelector { Enable = false },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var hits = MapPointsToHits(points, collection, scoreThreshold: 0f);
        if (!string.IsNullOrEmpty(pathGlob))
        {
            hits = hits.Where(h => MatchPathGlob(h.RelPath, pathGlob)).Take(limit).ToArray();
        }
        else
        {
            hits = hits.Take(limit).ToArray();
        }

        return hits;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHit>> FindOutlierChunksAsync(
        string collection,
        IReadOnlyList<string>? contextChunkIds = null,
        int limit = 5,
        string? language = null,
        string? pathGlob = null,
        float? maxSimilarity = null,
        int? maxContextSamples = null,
        CancellationToken cancellationToken = default)
    {
        var maxSim = maxSimilarity ?? _discovery.OutlierMaxSimilarity;
        var maxCtx = maxContextSamples ?? _discovery.OutlierMaxContextSamples;
        var contextSamples = await SampleContextDenseVectorsAsync(
            collection, contextChunkIds, pathGlob, maxCtx, cancellationToken).ConfigureAwait(false);
        if (contextSamples.Count == 0)
        {
            throw new ArgumentException(
                $"No context vectors found in collection '{collection}'. " +
                "Provide context_chunk_ids and/or ensure the collection has indexed chunks.");
        }

        var contextChunkIdSet = contextSamples.Select(s => s.ChunkId).ToHashSet(StringComparer.Ordinal);
        var centroid = ComputeCentroid(contextSamples.Select(s => s.Dense).ToArray());
        var negativeIds = contextSamples
            .Select(s => new PointId { Uuid = s.PointId })
            .ToArray();

        var input = new RecommendInput { Strategy = RecommendStrategy.BestScore };
        foreach (var id in negativeIds)
        {
            input.Negative.Add(id);
        }

        var fetchLimit = string.IsNullOrEmpty(pathGlob) ? limit * 2 : limit * 3;
        var points = await _client.Value.QueryAsync(
            collection,
            query: input,
            usingVector: "dense",
            filter: BuildLanguageFilter(language),
            searchParams: BuildDenseSearchParams(),
            limit: (ulong)fetchLimit,
            payloadSelector: new WithPayloadSelector { Enable = true },
            vectorsSelector: (WithVectorsSelector)new[] { "dense" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var candidates = new List<SearchHit>();
        foreach (var point in points)
        {
            var payload = point.Payload;
            var chunkId = GetString(payload, "chunk_id");
            if (string.IsNullOrEmpty(chunkId) || contextChunkIdSet.Contains(chunkId))
            {
                continue;
            }

            var dense = ExtractDenseVector(point.Vectors);
            var similarity = CosineSimilarity(dense, centroid);
            if (similarity > maxSim)
            {
                continue;
            }

            candidates.Add(new SearchHit(
                new ChunkId(chunkId),
                similarity,
                GetString(payload, "rel_path"),
                GetString(payload, "language"),
                GetInt(payload, "start_line"),
                GetInt(payload, "end_line"),
                GetOptionalString(payload, "symbol_name"),
                GetString(payload, "symbol_type", "other"),
                GetString(payload, "content"),
                collection));
        }

        candidates.Sort(static (a, b) => a.Score.CompareTo(b.Score));
        if (!string.IsNullOrEmpty(pathGlob))
        {
            candidates = candidates.Where(c => MatchPathGlob(c.RelPath, pathGlob)).ToList();
        }

        return candidates.Take(limit).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHit>> FindCallersInCollectionsAsync(
        string method,
        IReadOnlyList<string> collections,
        string? receiver = null,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default)
    {
        var token = string.IsNullOrEmpty(receiver) ? method : $"{receiver}.{method}";
        var tasks = collections.Select(async coll =>
        {
            try
            {
                if (!await CollectionExistsAsync(coll, cancellationToken).ConfigureAwait(false))
                {
                    return (IReadOnlyList<SearchHit>)Array.Empty<SearchHit>();
                }

                var result = await _client.Value.ScrollAsync(
                    coll,
                    filter: new Filter
                    {
                        Must =
                        {
                            new Condition
                            {
                                Field = new FieldCondition
                                {
                                    Key = "callees",
                                    Match = new GrpcMatch { Keyword = token },
                                },
                            },
                        },
                    },
                    limit: (uint)limitPerCollection,
                    payloadSelector: new WithPayloadSelector { Enable = true },
                    vectorsSelector: new WithVectorsSelector { Enable = false },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                return result.Result
                    .Select(p => new SearchHit(
                        new ChunkId(GetString(p.Payload, "chunk_id")),
                        0.0,
                        GetString(p.Payload, "rel_path"),
                        GetString(p.Payload, "language"),
                        GetInt(p.Payload, "start_line"),
                        GetInt(p.Payload, "end_line"),
                        GetOptionalString(p.Payload, "symbol_name"),
                        GetString(p.Payload, "symbol_type", "other"),
                        GetString(p.Payload, "content"),
                        coll))
                    .Where(h => !string.IsNullOrEmpty(h.Id.Value))
                    .ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "callers_scroll_error collection={Collection}", coll);
                return Array.Empty<SearchHit>();
            }
        });

        var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
        return batches.SelectMany(b => b).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ScrollChunksByPathsAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        IReadOnlyList<string>? payloadFields = null,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        if (relPaths.Count == 0
            || !await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return Array.Empty<IReadOnlyDictionary<string, string>>();
        }

        WithPayloadSelector payloadSelector = payloadFields is { Count: > 0 }
            ? new WithPayloadSelector
            {
                Include = new PayloadIncludeSelector { Fields = { payloadFields } },
            }
            : new WithPayloadSelector { Enable = true };

        try
        {
            var result = await _client.Value.ScrollAsync(
                collection,
                filter: new Filter
                {
                    Should =
                    {
                        relPaths.Select(path => new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key = "rel_path",
                                Match = new GrpcMatch { Keyword = path },
                            },
                        }),
                    },
                },
                limit: (uint)limit,
                payloadSelector: payloadSelector,
                vectorsSelector: new WithVectorsSelector { Enable = false },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var rows = new List<IReadOnlyDictionary<string, string>>(result.Result.Count);
            foreach (var point in result.Result)
            {
                var dict = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in point.Payload)
                {
                    if (kv.Value.KindCase == Value.KindOneofCase.StringValue)
                    {
                        dict[kv.Key] = kv.Value.StringValue;
                    }
                    else if (kv.Value.KindCase == Value.KindOneofCase.IntegerValue)
                    {
                        dict[kv.Key] = kv.Value.IntegerValue.ToString();
                    }
                }

                rows.Add(dict);
            }

            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "scroll_chunks_by_paths_error collection={Collection} n_paths={Count}",
                collection,
                relPaths.Count);
            return Array.Empty<IReadOnlyDictionary<string, string>>();
        }
    }

    private async Task EnsurePayloadIndexesAsync(string collection, CancellationToken cancellationToken)
    {
        foreach (var field in IndexedPayloadFields)
        {
            try
            {
                await _client.Value.CreatePayloadIndexAsync(
                    collection,
                    field,
                    PayloadSchemaType.Keyword,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "payload_index_skip collection={Collection} field={Field}", collection, field);
            }
        }
    }

    private bool NeedsRecreate(CollectionInfo info)
    {
        var vectorsConfig = info.Config.Params.VectorsConfig;
        if (vectorsConfig.ConfigCase != VectorsConfig.ConfigOneofCase.ParamsMap
            || !vectorsConfig.ParamsMap.Map.TryGetValue("dense", out var dense))
        {
            return true;
        }

        var hasSparse = info.Config.Params.SparseVectorsConfig.Map.ContainsKey("sparse");
        var hasColbert = vectorsConfig.ParamsMap.Map.ContainsKey("colbert");
        var colbertSize = 0;
        if (hasColbert && vectorsConfig.ParamsMap.Map.TryGetValue("colbert", out var colbert))
        {
            colbertSize = (int)colbert.Size;
        }

        var hasQuant = info.Config.QuantizationConfig?.QuantizationCase
            == QuantizationConfig.QuantizationOneofCase.Scalar;

        var decision = EvaluateCollectionSchema(
            denseSize: (int)dense.Size,
            hasSparse: hasSparse,
            hasColbert: hasColbert,
            colbertSize: colbertSize,
            hasQuantization: hasQuant,
            expectedDenseSize: _embedding.DenseVectorSize,
            hybridSearch: _embedding.HybridSearch,
            rerankEnabled: _embedding.RerankEnabled,
            expectedColbertTokenSize: ColbertTokenSize,
            quantizationEnabled: _qdrant.Quantization);

        if (decision.ColbertMismatch)
        {
            _logger.LogWarning(
                "Qdrant collection colbert mismatch (has_colbert={Has} rerank_enabled={Rerank}). Re-index after pull.",
                hasColbert,
                _embedding.RerankEnabled);
        }

        if (decision.ColbertDimMismatch)
        {
            _logger.LogWarning(
                "Qdrant colbert dim mismatch (collection={Have} want={Want}). Re-index after pull.",
                colbertSize,
                ColbertTokenSize);
        }

        return decision.NeedsRecreate;
    }

    /// <summary>
    /// Hybrid prefetch limit: <see cref="EmbeddingOptions.RerankPrefetch"/> when ColBERT
    /// rerank is active, otherwise <c>top_k * PrefetchMultiplier</c>.
    /// </summary>
    internal static ulong ResolveSearchPrefetchLimit(
        bool usedRerank,
        int rerankPrefetch,
        int topK,
        int prefetchMultiplier) =>
        (ulong)(usedRerank
            ? Math.Max(1, rerankPrefetch)
            : topK * Math.Max(1, prefetchMultiplier));

    /// <summary>
    /// Adaptive ColBERT skip when hybrid probe has ≥2 hits and top-1 vs top-2 RRF gap
    /// meets the threshold (Python <c>test_adaptive_rerank_*</c> parity).
    /// </summary>
    internal static bool ShouldSkipColbertAfterProbe(
        IReadOnlyList<ScoredPoint> probe,
        float gapThreshold) =>
        probe.Count >= 2 && probe[0].Score - probe[1].Score >= gapThreshold;

    /// <summary>
    /// Schema recreate decision for dense/sparse/colbert/quantization parity
    /// (Python <c>ensure_collection</c> mismatch cases).
    /// </summary>
    internal static CollectionSchemaDecision EvaluateCollectionSchema(
        int denseSize,
        bool hasSparse,
        bool hasColbert,
        int colbertSize,
        bool hasQuantization,
        int expectedDenseSize,
        bool hybridSearch,
        bool rerankEnabled,
        int expectedColbertTokenSize,
        bool quantizationEnabled)
    {
        if (denseSize != expectedDenseSize)
        {
            return new CollectionSchemaDecision(NeedsRecreate: true);
        }

        if (hasSparse != hybridSearch)
        {
            return new CollectionSchemaDecision(NeedsRecreate: true);
        }

        if (hasColbert != rerankEnabled)
        {
            return new CollectionSchemaDecision(NeedsRecreate: true, ColbertMismatch: true);
        }

        if (hasColbert && colbertSize != expectedColbertTokenSize)
        {
            return new CollectionSchemaDecision(NeedsRecreate: true, ColbertDimMismatch: true);
        }

        if (hasQuantization != quantizationEnabled)
        {
            return new CollectionSchemaDecision(NeedsRecreate: true);
        }

        return new CollectionSchemaDecision(NeedsRecreate: false);
    }

    private SearchParams BuildDenseSearchParams()
    {
        var searchParams = new SearchParams { HnswEf = (ulong)_qdrant.HnswEf };
        if (_qdrant.Quantization)
        {
            searchParams.Quantization = new QuantizationSearchParams
            {
                Rescore = true,
                Oversampling = _qdrant.QuantOversampling,
            };
        }

        return searchParams;
    }

    private static Filter? BuildLanguageFilter(string? language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return null;
        }

        return new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "language",
                        Match = new GrpcMatch { Keyword = language },
                    },
                },
            },
        };
    }

    private static IReadOnlyList<SearchHit> MapPointsToHits(
        IReadOnlyList<ScoredPoint> points,
        string collection,
        float scoreThreshold)
    {
        var hits = new List<SearchHit>(points.Count);
        foreach (var point in points)
        {
            if (point.Score < scoreThreshold)
            {
                continue;
            }

            var payload = point.Payload;
            var chunkId = GetString(payload, "chunk_id");
            if (string.IsNullOrEmpty(chunkId))
            {
                continue;
            }

            hits.Add(new SearchHit(
                new ChunkId(chunkId),
                point.Score,
                GetString(payload, "rel_path"),
                GetString(payload, "language"),
                GetInt(payload, "start_line"),
                GetInt(payload, "end_line"),
                GetOptionalString(payload, "symbol_name"),
                GetString(payload, "symbol_type", "other"),
                GetString(payload, "content"),
                collection));
        }

        return hits;
    }

    private static ChunkPayload MapPayload(IDictionary<string, Value> payload, string collection) =>
        new(
            GetString(payload, "chunk_id"),
            GetString(payload, "rel_path"),
            GetString(payload, "content"),
            GetInt(payload, "start_line"),
            GetInt(payload, "end_line"),
            GetString(payload, "language"),
            GetString(payload, "file_sha256"),
            GetOptionalString(payload, "symbol_name"),
            GetString(payload, "symbol_type", "other"),
            collection);

    private static string GetString(IDictionary<string, Value> payload, string key, string defaultValue = "")
    {
        if (!payload.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return value.KindCase switch
        {
            Value.KindOneofCase.StringValue => value.StringValue,
            _ => defaultValue,
        };
    }

    private static string? GetOptionalString(IDictionary<string, Value> payload, string key)
    {
        var value = GetString(payload, key);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static int GetInt(IDictionary<string, Value> payload, string key)
    {
        if (!payload.TryGetValue(key, out var value))
        {
            return 0;
        }

        return value.KindCase switch
        {
            Value.KindOneofCase.IntegerValue => (int)value.IntegerValue,
            Value.KindOneofCase.DoubleValue => (int)value.DoubleValue,
            _ => 0,
        };
    }

    private static PointStruct ToPoint(
        EmbeddedChunk chunk,
        bool omitCallees = false,
        IReadOnlyList<string>? graphNodeIds = null)
    {
        var namedVectors = new Dictionary<string, Vector>
        {
            ["dense"] = chunk.DenseVector.ToArray(),
        };

        if (chunk.SparseVector is not null)
        {
            namedVectors["sparse"] = (
                chunk.SparseVector.Values.ToArray(),
                chunk.SparseVector.Indices.Select(i => i).ToArray());
        }

        if (chunk.ColbertVector is { Count: > 0 })
        {
            namedVectors["colbert"] = ToMultiVector(chunk.ColbertVector);
        }

        var payload = new Dictionary<string, Value>
        {
            ["chunk_id"] = chunk.Chunk.Id.Value,
            ["rel_path"] = chunk.Chunk.RelPath,
            ["content"] = chunk.Chunk.Content,
            ["start_line"] = chunk.Chunk.StartLine,
            ["end_line"] = chunk.Chunk.EndLine,
            ["language"] = chunk.Chunk.Language,
            ["file_sha256"] = chunk.Chunk.FileSha256,
            ["file_mtime"] = 0.0,
            ["symbol_name"] = chunk.Chunk.SymbolName ?? string.Empty,
            ["symbol_type"] = string.IsNullOrEmpty(chunk.Chunk.SymbolType) ? "other" : chunk.Chunk.SymbolType,
        };

        if (!omitCallees)
        {
            var callees = chunk.Chunk.Callees.Count == 0
                ? Array.Empty<Value>()
                : chunk.Chunk.Callees.Select(c => (Value)c).ToArray();
            payload["callees"] = callees;
        }

        if (graphNodeIds is not null)
        {
            payload["graph_node_ids"] = graphNodeIds.Count == 0
                ? Array.Empty<Value>()
                : graphNodeIds.Select(id => (Value)id).ToArray();
        }

        var point = new PointStruct
        {
            Id = new PointId { Uuid = ChunkIdToPointUuid(chunk.Chunk.Id.Value) },
            Vectors = namedVectors,
        };
        foreach (var (k, v) in payload)
        {
            point.Payload[k] = v;
        }

        return point;
    }

    private async Task SetCollectionMetadataFlagAsync(
        string collection,
        string key,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var metadata = new Dictionary<string, Value>
        {
            [key] = enabled,
        };
        await _client.Value.UpdateCollectionAsync(
            collection,
            metadata: metadata,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ReadCollectionMetadataFlagAsync(
        string collection,
        string key,
        CancellationToken cancellationToken)
    {
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var info = await _client.Value.GetCollectionInfoAsync(collection, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return MetadataFlagTrue(info.Config?.Metadata, key);
    }

    private static bool MetadataFlagTrue(
        IReadOnlyDictionary<string, Value>? metadata,
        string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value))
        {
            return false;
        }

        return value.KindCase switch
        {
            Value.KindOneofCase.BoolValue => value.BoolValue,
            Value.KindOneofCase.StringValue => string.Equals(value.StringValue, "true", StringComparison.OrdinalIgnoreCase),
            Value.KindOneofCase.IntegerValue => value.IntegerValue != 0,
            _ => false,
        };
    }

    private async Task<IReadOnlyList<ContextDenseSample>> SampleContextDenseVectorsAsync(
        string collection,
        IReadOnlyList<string>? contextChunkIds,
        string? pathGlob,
        int maxSamples,
        CancellationToken cancellationToken)
    {
        var samples = new List<ContextDenseSample>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chunkIds = contextChunkIds ?? Array.Empty<string>();

        if (chunkIds.Count > 0)
        {
            var pointIds = chunkIds
                .Select(id => new PointId { Uuid = ChunkIdToPointUuid(id) })
                .ToArray();
            var records = await _client.Value.RetrieveAsync(
                collection,
                pointIds,
                new WithPayloadSelector { Enable = true },
                (WithVectorsSelector)new[] { "dense" },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var byId = records.ToDictionary(r => r.Id.Uuid, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < chunkIds.Count; i++)
            {
                if (!byId.TryGetValue(pointIds[i].Uuid, out var record))
                {
                    continue;
                }

                var dense = ExtractDenseVector(record.Vectors);
                if (dense.Count == 0)
                {
                    continue;
                }

                samples.Add(new ContextDenseSample(chunkIds[i], pointIds[i].Uuid, dense));
                seen.Add(chunkIds[i]);
                if (samples.Count >= maxSamples)
                {
                    return samples;
                }
            }
        }

        var remaining = maxSamples - samples.Count;
        if (remaining <= 0 || (chunkIds.Count > 0 && string.IsNullOrEmpty(pathGlob)))
        {
            return samples;
        }

        PointId? offset = null;
        while (remaining > 0)
        {
            var result = await _client.Value.ScrollAsync(
                collection,
                limit: (uint)Math.Min(remaining * 2, 256),
                offset: offset,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: (WithVectorsSelector)new[] { "dense" },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.Result.Count == 0)
            {
                break;
            }

            foreach (var point in result.Result)
            {
                var cid = GetString(point.Payload, "chunk_id");
                if (string.IsNullOrEmpty(cid) || !seen.Add(cid))
                {
                    continue;
                }

                var relPath = GetString(point.Payload, "rel_path");
                if (!string.IsNullOrEmpty(pathGlob) && !MatchPathGlob(relPath, pathGlob))
                {
                    continue;
                }

                var dense = ExtractDenseVector(point.Vectors);
                if (dense.Count == 0)
                {
                    continue;
                }

                samples.Add(new ContextDenseSample(cid, point.Id.Uuid, dense));
                remaining--;
                if (remaining <= 0)
                {
                    break;
                }
            }

            if (result.NextPageOffset is null)
            {
                break;
            }

            offset = result.NextPageOffset;
        }

        return samples;
    }

    private static void AddRecommendExamples(
        Google.Protobuf.Collections.RepeatedField<VectorInput> target,
        IReadOnlyList<RecommendExample> examples)
    {
        foreach (var example in examples)
        {
            if (example.DenseVector is not null)
            {
                target.Add(example.DenseVector as float[] ?? example.DenseVector.ToArray());
            }
            else if (!string.IsNullOrEmpty(example.ChunkId))
            {
                target.Add(new PointId { Uuid = ChunkIdToPointUuid(example.ChunkId) });
            }
        }
    }

    private static IReadOnlyList<float> ExtractDenseVector(VectorsOutput? vectors)
    {
        if (vectors is null)
        {
            return Array.Empty<float>();
        }

        if (vectors.VectorsOptionsCase == VectorsOutput.VectorsOptionsOneofCase.Vectors)
        {
            if (vectors.Vectors.Vectors.TryGetValue("dense", out var dense))
            {
                return DenseFloats(dense);
            }
        }
        else if (vectors.VectorsOptionsCase == VectorsOutput.VectorsOptionsOneofCase.Vector)
        {
            return DenseFloats(vectors.Vector);
        }

        return Array.Empty<float>();
    }

    private static IReadOnlyList<float> DenseFloats(VectorOutput vector)
    {
        if (vector.Dense?.Data is { Count: > 0 } denseData)
        {
            return denseData.ToArray();
        }

#pragma warning disable CS0612 // Data obsolete but still populated by some server versions
        if (vector.Data.Count > 0)
        {
            return vector.Data.ToArray();
        }
#pragma warning restore CS0612

        return Array.Empty<float>();
    }

    private static IReadOnlyList<float> ComputeCentroid(IReadOnlyList<IReadOnlyList<float>> vectors)
    {
        if (vectors.Count == 0)
        {
            return Array.Empty<float>();
        }

        var dim = vectors[0].Count;
        var centroid = new float[dim];
        foreach (var vec in vectors)
        {
            for (var i = 0; i < dim; i++)
            {
                centroid[i] += vec[i];
            }
        }

        var n = (float)vectors.Count;
        for (var i = 0; i < dim; i++)
        {
            centroid[i] /= n;
        }

        return centroid;
    }

    private static double CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count == 0 || b.Count == 0 || a.Count != b.Count)
        {
            return 0.0;
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
        {
            return 0.0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static bool MatchPathGlob(string relPath, string pattern)
    {
        // fnmatch-style: * and ? only (Python fnmatch parity for recommend/outlier path_glob).
        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(relPath, regex, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private sealed record ContextDenseSample(string ChunkId, string PointId, IReadOnlyList<float> Dense);

    internal static string ChunkIdToPointUuid(string chunkId)
    {
        var namespaceBytes = ChunkNamespace.ToByteArray();
        ReverseGuidTimeFields(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(chunkId);
        var hash = SHA1.HashData(namespaceBytes.Concat(nameBytes).ToArray());
        var bytes = hash.Take(16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        ReverseGuidTimeFields(bytes);
        return new Guid(bytes).ToString();
    }

    private static void ReverseGuidTimeFields(byte[] bytes)
    {
        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);
    }

    private static QdrantClient CreateClient(QdrantOptions options)
    {
        try
        {
            var (host, port, https) = QdrantGrpcEndpoint.Parse(options.Url);
            var grpcTimeout = options.TimeoutSeconds > 0
                ? TimeSpan.FromSeconds(options.TimeoutSeconds)
                : TimeSpan.FromSeconds(120);
            return new QdrantClient(
                host: host,
                port: port,
                https: https,
                grpcTimeout: grpcTimeout);
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or RpcException)
        {
            throw new VectorStoreException($"Invalid Qdrant URL '{options.Url}'.", ex);
        }
    }
}
