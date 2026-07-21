using CodebaseIndexer.Application.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.Caching.Memory;

namespace CodebaseIndexer.Application.Services;

/// <summary>Scroll/stats-backed read tools with short-lived memory cache.</summary>
public sealed class CollectionQueryService : ICollectionQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IVectorStore _store;
    private readonly IMemoryCache _cache;

    /// <summary>Creates the collection query service.</summary>
    public CollectionQueryService(IVectorStore store, IMemoryCache cache)
    {
        _store = store;
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<object> GetChunkAsync(
        string chunkId,
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        var payload = await _store.FindChunkByIdAsync(chunkId, collection, cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            var scope = collection ?? "any collection";
            return new ChunkNotFoundResponse($"Chunk '{chunkId}' not found in {scope}.");
        }

        return new Dictionary<string, object?>
        {
            ["chunk_id"] = payload.ChunkId,
            ["rel_path"] = payload.RelPath,
            ["content"] = payload.Content,
            ["start_line"] = payload.StartLine,
            ["end_line"] = payload.EndLine,
            ["language"] = payload.Language,
            ["file_sha256"] = payload.FileSha256,
            ["symbol_name"] = payload.SymbolName ?? string.Empty,
            ["symbol_type"] = payload.SymbolType,
            ["file_mtime"] = 0.0,
        };
    }

    /// <inheritdoc />
    public async Task<object> GetFileOutlineAsync(
        string relPath,
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        var coll = collection ?? "codebase";
        var cacheKey = $"outline:{coll}:{relPath}";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached is not null)
        {
            return cached;
        }

        var symbols = await _store.ScrollFileSymbolsAsync(coll, relPath, cancellationToken)
            .ConfigureAwait(false);
        if (symbols.Count == 0)
        {
            return new FileOutlineErrorResponse(
                $"No symbols found for '{relPath}' in collection '{coll}'.",
                "Check the path with search_symbols or list_collections.");
        }

        var response = new FileOutlineResponse(
            coll,
            relPath,
            symbols.Count,
            symbols.Select(s => new FileOutlineSymbol(
                s.ChunkId,
                s.SymbolName,
                s.SymbolType,
                s.StartLine,
                s.EndLine,
                s.Language)).ToArray());

        _cache.Set(cacheKey, response, CacheTtl);
        return response;
    }

    /// <inheritdoc />
    public async Task<object> GetCollectionSummaryAsync(
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        var coll = collection ?? "codebase";
        var cacheKey = $"summary:{coll}";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached is not null)
        {
            return cached;
        }

        var rows = await _store.ScrollAllPayloadsAsync(coll, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return new CollectionSummaryErrorResponse(
                $"Collection '{coll}' is empty or does not exist.",
                "Use index_codebase to index a project first.");
        }

        var filesByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        var langCounter = new Dictionary<string, int>(StringComparer.Ordinal);
        var symbolTypeCounter = new Dictionary<string, int>(StringComparer.Ordinal);
        var chunksPerFile = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var language = string.IsNullOrEmpty(row.Language) ? "unknown" : row.Language;
            var symbolType = string.IsNullOrEmpty(row.SymbolType) ? "other" : row.SymbolType;

            if (!filesByPath.ContainsKey(row.RelPath))
            {
                filesByPath[row.RelPath] = language;
                langCounter[language] = langCounter.GetValueOrDefault(language) + 1;
            }

            symbolTypeCounter[symbolType] = symbolTypeCounter.GetValueOrDefault(symbolType) + 1;
            chunksPerFile[row.RelPath] = chunksPerFile.GetValueOrDefault(row.RelPath) + 1;
        }

        var response = new CollectionSummaryResponse(
            coll,
            filesByPath.Count,
            rows.Count,
            langCounter.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            symbolTypeCounter.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            TopLevelDirs(filesByPath.Keys.ToList(), depth: 2),
            chunksPerFile
                .OrderByDescending(kv => kv.Value)
                .Take(10)
                .Select(kv => new TopChunkedFile(kv.Key, kv.Value))
                .ToArray());

        _cache.Set(cacheKey, response, CacheTtl);
        return response;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectionListItem>> ListCollectionsAsync(
        CancellationToken cancellationToken = default)
    {
        var stats = await _store.ListCollectionStatsAsync(cancellationToken).ConfigureAwait(false);
        return stats.Select(s => new CollectionListItem(
            s.Name,
            s.VectorCount,
            s.DiskSizeMb,
            s.DenseEmbedModel,
            s.SparseEmbedModel,
            s.Hybrid,
            s.RerankEnabled,
            string.IsNullOrEmpty(s.ColbertEmbedModel) ? null : s.ColbertEmbedModel)).ToArray();
    }

    internal static IReadOnlyList<string> TopLevelDirs(IReadOnlyList<string> relPaths, int depth = 2)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in relPaths)
        {
            var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var dirParts = parts.Length > 0 ? parts[..^1] : [];
            for (var d = 1; d <= Math.Min(depth, dirParts.Length); d++)
            {
                var prefix = string.Join('/', dirParts.Take(d));
                if (!string.IsNullOrEmpty(prefix))
                {
                    seen.Add(prefix);
                }
            }
        }

        return seen.OrderBy(s => s, StringComparer.Ordinal).ToArray();
    }
}
