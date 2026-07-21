using System.Security.Cryptography;
using System.Text;
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

namespace CodebaseIndexer.Infrastructure.Qdrant;

/// <summary>Qdrant-backed implementation of <see cref="IVectorStore"/>.</summary>
public sealed class QdrantVectorStore : IVectorStore
{
    private static readonly Guid ChunkNamespace = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    private static readonly string[] IndexedPayloadFields =
        ["rel_path", "chunk_id", "symbol_name", "language", "callees"];

    private readonly QdrantOptions _qdrant;
    private readonly EmbeddingOptions _embedding;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly Lazy<QdrantClient> _client;

    /// <summary>Creates a vector store client from Qdrant and embedding options.</summary>
    public QdrantVectorStore(
        IOptions<QdrantOptions> qdrant,
        IOptions<EmbeddingOptions> embedding,
        ILogger<QdrantVectorStore> logger)
    {
        _qdrant = qdrant.Value;
        _embedding = embedding.Value;
        _logger = logger;
        _client = new Lazy<QdrantClient>(() => CreateClient(_qdrant));
    }

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
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var points = chunks.Select(ToPoint).ToList();
        await _client.Value.UpsertAsync(collection, points, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string collection,
        IReadOnlyList<float> denseVector,
        DomainSparseVector? sparseVector,
        int topK,
        string? language = null,
        float minScore = 0.5f,
        CancellationToken cancellationToken = default)
    {
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return Array.Empty<SearchHit>();
        }

        var queryFilter = BuildLanguageFilter(language);
        var denseParams = BuildDenseSearchParams();
        var usedHybrid = sparseVector is not null && _embedding.HybridSearch;
        var prefetchLimit = (ulong)(topK * Math.Max(1, _embedding.PrefetchMultiplier));
        var scoreThreshold = usedHybrid ? 0f : minScore;
        var denseArray = denseVector as float[] ?? denseVector.ToArray();

        IReadOnlyList<ScoredPoint> points;
        if (usedHybrid)
        {
            var sparseIndices = sparseVector!.Indices.Select(i => (uint)i).ToArray();
            var sparseValues = sparseVector.Values as float[] ?? sparseVector.Values.ToArray();
            points = await _client.Value.QueryAsync(
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
                limit: (ulong)topK,
                payloadSelector: new WithPayloadSelector { Enable = true },
                vectorsSelector: new WithVectorsSelector { Enable = false },
                cancellationToken: cancellationToken).ConfigureAwait(false);
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
                            Match = new Match { Keyword = chunkId },
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
                                    Match = new Match { Keyword = relPath },
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
                                    Match = new Match { Keyword = symbolName },
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

        return new CollectionStats(
            collection,
            (long)info.PointsCount,
            0,
            _embedding.DenseModel,
            _embedding.SparseModel,
            EmbedderBackendKeys.Dense.Tei,
            _embedding.HybridSearch,
            _embedding.RerankEnabled);
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
                            Match = new Match { Keyword = path },
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

        if ((int)dense.Size != _embedding.DenseVectorSize)
        {
            return true;
        }

        var hasSparse = info.Config.Params.SparseVectorsConfig.Map.ContainsKey("sparse");
        if (hasSparse != _embedding.HybridSearch)
        {
            return true;
        }

        var hasQuant = info.Config.QuantizationConfig?.QuantizationCase
            == QuantizationConfig.QuantizationOneofCase.Scalar;
        if (hasQuant != _qdrant.Quantization)
        {
            return true;
        }

        return false;
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
                        Match = new Match { Keyword = language },
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

    private static PointStruct ToPoint(EmbeddedChunk chunk)
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

        return new PointStruct
        {
            Id = new PointId { Uuid = ChunkIdToPointUuid(chunk.Chunk.Id.Value) },
            Vectors = namedVectors,
            Payload =
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
            },
        };
    }

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
            return new QdrantClient(host: host, port: port, https: https);
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or RpcException)
        {
            throw new VectorStoreException($"Invalid Qdrant URL '{options.Url}'.", ex);
        }
    }
}
