using System.Security.Cryptography;
using System.Text;
using CodebaseIndexer.Domain.Exceptions;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using CodebaseIndexer.Infrastructure.Configuration;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace CodebaseIndexer.Infrastructure.Qdrant;

public sealed class QdrantVectorStore : IVectorStore
{
    private static readonly Guid ChunkNamespace = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    private readonly Settings _settings;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly Lazy<QdrantClient> _client;

    public QdrantVectorStore(IOptions<Settings> settings, ILogger<QdrantVectorStore> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _client = new Lazy<QdrantClient>(() => CreateClient(_settings));
    }

    public async ValueTask<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default)
    {
        var collections = await _client.Value.ListCollectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return collections.Contains(collection);
    }

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
                    return;
                }
            }

            await _client.Value.DeleteCollectionAsync(collection, cancellationToken: cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Recreated Qdrant collection {Collection}", collection);
        }

        var vectorsConfig = new VectorParamsMap
        {
            Map =
            {
                ["dense"] = new VectorParams
                {
                    Size = (ulong)_settings.DenseEmbedVectorSize,
                    Distance = Distance.Cosine,
                    OnDisk = _settings.VectorsOnDisk,
                },
            },
        };

        SparseVectorConfig? sparseVectorsConfig = null;
        if (_settings.HybridSearch)
        {
            sparseVectorsConfig = new SparseVectorConfig
            {
                Map =
                {
                    ["sparse"] = new SparseVectorParams
                    {
                        Index = new SparseIndexConfig { OnDisk = _settings.SparseOnDisk },
                    },
                },
            };
        }

        await _client.Value.CreateCollectionAsync(
            collection,
            vectorsConfig: vectorsConfig,
            sparseVectorsConfig: sparseVectorsConfig,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

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

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string collection,
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Phase 1 stub — hybrid search lands in Phase 3.
        _ = query;
        _ = limit;
        if (!await CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        {
            return Array.Empty<SearchHit>();
        }

        return Array.Empty<SearchHit>();
    }

    public async Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var collections = await _client.Value.ListCollectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return collections.OrderBy(c => c, StringComparer.Ordinal).ToArray();
    }

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
            _settings.DenseEmbedModel,
            _settings.SparseEmbedModel,
            "tei",
            _settings.HybridSearch,
            _settings.RerankEnabled);
    }

    private bool NeedsRecreate(CollectionInfo info)
    {
        var vectorsConfig = info.Config.Params.VectorsConfig;
        if (vectorsConfig.ConfigCase != VectorsConfig.ConfigOneofCase.ParamsMap
            || !vectorsConfig.ParamsMap.Map.TryGetValue("dense", out var dense))
        {
            return true;
        }

        if ((int)dense.Size != _settings.DenseEmbedVectorSize)
        {
            return true;
        }

        var hasSparse = info.Config.Params.SparseVectorsConfig.Map.ContainsKey("sparse");
        return hasSparse != _settings.HybridSearch;
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
                ["symbol_name"] = chunk.Chunk.SymbolName ?? "",
                ["symbol_type"] = "",
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

    private static QdrantClient CreateClient(Settings settings)
    {
        try
        {
            if (Uri.TryCreate(settings.QdrantUrl, UriKind.Absolute, out var uri))
            {
                return new QdrantClient(host: uri.Host, port: uri.Port, https: uri.Scheme == "https");
            }
        }
        catch (Exception ex) when (ex is UriFormatException or RpcException)
        {
            throw new VectorStoreException($"Invalid Qdrant URL '{settings.QdrantUrl}'.", ex);
        }

        throw new VectorStoreException($"Invalid Qdrant URL '{settings.QdrantUrl}'.");
    }
}
