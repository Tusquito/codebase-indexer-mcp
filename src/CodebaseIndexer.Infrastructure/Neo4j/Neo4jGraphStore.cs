using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodebaseIndexer.Infrastructure.Neo4j;

/// <summary>Neo4j-backed <see cref="IGraphStore"/> (Python <c>Neo4jStorage</c>).</summary>
public sealed class Neo4jGraphStore : IGraphStore, IAsyncDisposable, IDisposable
{
    private static readonly string[] SchemaStatements =
    [
        "CREATE CONSTRAINT chunk_id_unique IF NOT EXISTS FOR (c:Chunk) REQUIRE c.chunk_id IS UNIQUE",
        "CREATE CONSTRAINT file_collection_path_unique IF NOT EXISTS FOR (f:File) REQUIRE (f.collection, f.rel_path) IS UNIQUE",
        "CREATE CONSTRAINT symbol_qualified_name_unique IF NOT EXISTS FOR (s:Symbol) REQUIRE s.qualified_name IS UNIQUE",
        "CREATE CONSTRAINT collection_name_unique IF NOT EXISTS FOR (col:Collection) REQUIRE col.name IS UNIQUE",
        "CREATE CONSTRAINT artifact_key_unique IF NOT EXISTS FOR (a:Artifact) REQUIRE a.key IS UNIQUE",
        "CREATE INDEX endpoint_collection_path IF NOT EXISTS FOR (e:Endpoint) ON (e.collection, e.path)",
        "CREATE INDEX symbol_name_collection IF NOT EXISTS FOR (s:Symbol) ON (s.collection, s.name)",
        "CREATE INDEX calls_call_token IF NOT EXISTS FOR ()-[r:CALLS]-() ON (r.call_token)",
    ];

    private static readonly string[] NodePropKeys =
    [
        "chunk_id", "qualified_name", "name", "kind", "rel_path", "path",
        "method", "language", "start_line", "end_line", "collection",
    ];

    private static readonly string[] NodeKeyFields =
        ["chunk_id", "qualified_name", "path", "rel_path", "name", "key"];

    private readonly GraphOptions _options;
    private readonly IDriver _driver;
    private readonly ILogger<Neo4jGraphStore> _logger;
    private readonly bool _ownsDriver;
    private bool _schemaReady;

    /// <summary>Creates a Neo4j graph store with an injected driver.</summary>
    /// <param name="driver">Bolt driver.</param>
    /// <param name="options">Graph options.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="ownsDriver">When true, dispose the driver with this store.</param>
    public Neo4jGraphStore(
        IDriver driver,
        IOptions<GraphOptions> options,
        ILogger<Neo4jGraphStore> logger,
        bool ownsDriver = false)
    {
        _driver = driver;
        _options = options.Value;
        _logger = logger;
        _ownsDriver = ownsDriver;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsDriver)
        {
            _driver.Dispose();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_ownsDriver)
        {
            _driver.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_options.Enabled);

    /// <inheritdoc />
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || _schemaReady)
        {
            return;
        }

        await using var session = _driver.AsyncSession(o => o.WithDatabase(_options.Neo4jDatabase));
        foreach (var statement in SchemaStatements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(statement).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        _schemaReady = true;
        _logger.LogInformation("neo4j_schema_ready database={Database}", _options.Neo4jDatabase);
    }

    /// <inheritdoc />
    public async Task DeleteFilesAsync(
        string collection,
        IReadOnlyList<string> relPaths,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || relPaths.Count == 0)
        {
            return;
        }

        await using var session = _driver.AsyncSession(o => o.WithDatabase(_options.Neo4jDatabase));
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(
                """
                UNWIND $paths AS rel_path
                MATCH (f:File {collection: $collection, rel_path: rel_path})
                OPTIONAL MATCH (f)<-[:IN_FILE]-(ch:Chunk)
                DETACH DELETE ch, f
                """,
                new { collection, paths = relPaths.ToArray() }).ConfigureAwait(false);
        }).ConfigureAwait(false);
        _logger.LogDebug("neo4j_deleted_files collection={Collection} count={Count}", collection, relPaths.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHit>> FindCallersAsync(
        string method,
        IReadOnlyList<string> collections,
        string? receiver = null,
        int limitPerCollection = 10,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || collections.Count == 0)
        {
            return Array.Empty<SearchHit>();
        }

        var token = string.IsNullOrEmpty(receiver) ? method : $"{receiver}.{method}";
        var tasks = collections.Select(coll => QueryCallersAsync(coll, token, limitPerCollection, cancellationToken));
        var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
        return batches.SelectMany(b => b).ToArray();
    }

    /// <inheritdoc />
    public async Task<GraphExpansion> ExpandSubgraphAsync(
        IReadOnlyList<string> chunkIds,
        int maxHops,
        int maxNodes,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || chunkIds.Count == 0)
        {
            return GraphExpansion.Empty;
        }

        var hops = maxHops < 1 ? 1 : maxHops;
        if (hops > _options.MaxHops)
        {
            hops = _options.MaxHops;
        }

        var nodeCap = maxNodes < 1 ? 1 : maxNodes;
        // Hop bound is interpolated after clamp — cannot be a Cypher parameter.
        var query =
            "MATCH (c:Chunk) WHERE c.chunk_id IN $chunk_ids " +
            $"MATCH p = (c)-[*1..{hops}]-(m) " +
            "RETURN p LIMIT $max_nodes";

        var seedSet = chunkIds.ToHashSet(StringComparer.Ordinal);
        var nodesByKey = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var edges = new Dictionary<(string Type, string? From, string? To), GraphEdge>();
        var related = new Dictionary<string, string?>(StringComparer.Ordinal);

        await using var session = _driver.AsyncSession(o => o.WithDatabase(_options.Neo4jDatabase));
        var cursor = await session.RunAsync(
            query,
            new { chunk_ids = chunkIds.ToArray(), max_nodes = nodeCap }).ConfigureAwait(false);

        await foreach (var record in cursor.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!record.Values.TryGetValue("p", out var pathObj) || pathObj is not IPath path)
            {
                continue;
            }

            var nodesByElementId = path.Nodes.ToDictionary(n => n.ElementId, StringComparer.Ordinal);
            foreach (var node in path.Nodes)
            {
                var props = ToPropDict(node.Properties);
                var key = NodeKey(props);
                var labels = node.Labels.ToArray();
                if (key is not null && !nodesByKey.ContainsKey(key))
                {
                    nodesByKey[key] = new GraphNode(labels, key, FilterNodeProps(props));
                }

                if (labels.Contains("Chunk", StringComparer.Ordinal))
                {
                    var cid = props.GetValueOrDefault("chunk_id")?.ToString();
                    if (!string.IsNullOrEmpty(cid) && !seedSet.Contains(cid))
                    {
                        related.TryAdd(cid, props.GetValueOrDefault("collection")?.ToString());
                    }
                }
            }

            foreach (var rel in path.Relationships)
            {
                nodesByElementId.TryGetValue(rel.StartNodeElementId, out var startNode);
                nodesByElementId.TryGetValue(rel.EndNodeElementId, out var endNode);
                var fromKey = startNode is null ? null : NodeKey(ToPropDict(startNode.Properties));
                var toKey = endNode is null ? null : NodeKey(ToPropDict(endNode.Properties));
                var edgeId = (rel.Type, fromKey, toKey);
                edges.TryAdd(edgeId, new GraphEdge(rel.Type, fromKey, toKey));
            }
        }

        return new GraphExpansion(
            nodesByKey.Values.ToArray(),
            edges.Values.ToArray(),
            related.Keys.ToArray(),
            related.Where(kv => kv.Value is not null)
                .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public async Task WriteBatchAsync(GraphBatch batch, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        await using var session = _driver.AsyncSession(o => o.WithDatabase(_options.Neo4jDatabase));
        await session.ExecuteWriteAsync(async tx =>
        {
            await WriteBatchSessionAsync(tx, batch, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SearchHit>> QueryCallersAsync(
        string collection,
        string token,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var session = _driver.AsyncSession(o => o.WithDatabase(_options.Neo4jDatabase));
        var cursor = await session.RunAsync(
            """
            MATCH (col:Collection {name: $collection})<-[:IN_COLLECTION]-(f:File)
                  <-[:IN_FILE]-(ch:Chunk)-[r:CALLS]->(s:Symbol)
            WHERE r.call_token IN $tokens
            OPTIONAL MATCH (ch)-[:DEFINES]->(def:Symbol)
            RETURN ch.chunk_id AS chunk_id,
                   f.rel_path AS rel_path,
                   ch.start_line AS start_line,
                   ch.end_line AS end_line,
                   f.language AS language,
                   def.name AS symbol_name,
                   coalesce(def.kind, 'other') AS symbol_type
            LIMIT $limit
            """,
            new { collection, tokens = new[] { token }, limit }).ConfigureAwait(false);

        var results = new List<SearchHit>();
        await foreach (var rec in cursor.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunkId = rec["chunk_id"].As<string>();
            results.Add(new SearchHit(
                new ChunkId(chunkId),
                0,
                rec["rel_path"].As<string>() ?? string.Empty,
                rec["language"].As<string?>() ?? string.Empty,
                rec["start_line"].As<int?>() ?? 0,
                rec["end_line"].As<int?>() ?? 0,
                rec["symbol_name"].As<string?>(),
                rec["symbol_type"].As<string?>() ?? "other",
                string.Empty,
                collection));
        }

        return results;
    }

    private async Task WriteBatchSessionAsync(
        IAsyncQueryRunner tx,
        GraphBatch batch,
        CancellationToken cancellationToken)
    {
        var collection = batch.Collection;
        var size = Math.Max(1, _options.WriterBatch);

        await UnwindAsync(
            tx, batch.Files, size, cancellationToken,
            """
            UNWIND $files AS row
            MERGE (col:Collection {name: $collection})
            MERGE (f:File {collection: $collection, rel_path: row.rel_path})
            SET f.language = row.language,
                f.sha256 = row.sha256
            MERGE (f)-[:IN_COLLECTION]->(col)
            """,
            rows => new
            {
                collection,
                files = rows.Select(r => new { rel_path = r.RelPath, language = r.Language, sha256 = r.Sha256 }).ToArray(),
            }).ConfigureAwait(false);

        await UnwindAsync(
            tx, batch.Chunks, size, cancellationToken,
            """
            UNWIND $chunks AS row
            MATCH (f:File {collection: $collection, rel_path: row.rel_path})
            MERGE (ch:Chunk {chunk_id: row.chunk_id})
            SET ch.start_line = row.start_line,
                ch.end_line = row.end_line,
                ch.collection = $collection
            MERGE (ch)-[:IN_FILE]->(f)
            """,
            rows => new
            {
                collection,
                chunks = rows.Select(r => new
                {
                    chunk_id = r.ChunkId,
                    rel_path = r.RelPath,
                    start_line = r.StartLine,
                    end_line = r.EndLine,
                }).ToArray(),
            }).ConfigureAwait(false);

        await UnwindAsync(
            tx, batch.Defines, size, cancellationToken,
            """
            UNWIND $rows AS row
            MATCH (ch:Chunk {chunk_id: row.chunk_id})
            MERGE (s:Symbol {qualified_name: row.qualified_name})
            SET s.name = row.name,
                s.kind = row.kind,
                s.collection = $collection
            MERGE (ch)-[:DEFINES]->(s)
            """,
            rows => new
            {
                collection,
                rows = rows.Select(r => new
                {
                    chunk_id = r.ChunkId,
                    qualified_name = r.QualifiedName,
                    name = r.Name,
                    kind = r.Kind,
                }).ToArray(),
            }).ConfigureAwait(false);

        await UnwindAsync(
            tx, batch.Calls, size, cancellationToken,
            """
            UNWIND $rows AS row
            MATCH (ch:Chunk {chunk_id: row.chunk_id})
            MERGE (s:Symbol {qualified_name: row.qualified_name})
            ON CREATE SET s.name = row.name,
                          s.kind = 'callee',
                          s.collection = $collection
            SET s.collection = $collection
            MERGE (ch)-[r:CALLS]->(s)
            SET r.call_token = row.call_token
            """,
            rows => new
            {
                collection,
                rows = rows.Select(r => new
                {
                    chunk_id = r.ChunkId,
                    qualified_name = r.QualifiedName,
                    name = r.Name,
                    call_token = r.CallToken,
                }).ToArray(),
            }).ConfigureAwait(false);

        await UnwindAsync(
            tx, batch.Imports, size, cancellationToken,
            """
            UNWIND $rows AS row
            MATCH (f:File {collection: $collection, rel_path: row.rel_path})
            MERGE (s:Symbol {qualified_name: row.qualified_name})
            SET s.name = row.name,
                s.kind = 'import',
                s.collection = $collection
            MERGE (f)-[:IMPORTS]->(s)
            """,
            rows => new
            {
                collection,
                rows = rows.Select(r => new
                {
                    rel_path = r.RelPath,
                    qualified_name = r.QualifiedName,
                    name = r.Name,
                }).ToArray(),
            }).ConfigureAwait(false);

        await UnwindAsync(
            tx, batch.Endpoints, size, cancellationToken,
            """
            UNWIND $rows AS row
            MERGE (e:Endpoint {collection: $collection, path: row.path})
            SET e.method = coalesce(row.method, e.method, '')
            """,
            rows => new
            {
                collection,
                rows = rows.Select(r => new { path = r.Path, method = r.Method }).ToArray(),
            }).ConfigureAwait(false);

        await UnwindAsync(
            tx, batch.DeclaresEndpoint, size, cancellationToken,
            """
            UNWIND $rows AS row
            MATCH (ch:Chunk {chunk_id: row.chunk_id})
            MERGE (e:Endpoint {collection: $collection, path: row.path})
            MERGE (ch)-[:DECLARES_ENDPOINT]->(e)
            """,
            rows => new
            {
                collection,
                rows = rows.Select(r => new { chunk_id = r.ChunkId, path = r.Path }).ToArray(),
            }).ConfigureAwait(false);

        await UnwindAsync(
            tx, batch.HttpCalls, size, cancellationToken,
            """
            UNWIND $rows AS row
            MATCH (ch:Chunk {chunk_id: row.chunk_id})
            MERGE (e:Endpoint {collection: $collection, path: row.path})
            MERGE (ch)-[:HTTP_CALLS]->(e)
            """,
            rows => new
            {
                collection,
                rows = rows.Select(r => new { chunk_id = r.ChunkId, path = r.Path }).ToArray(),
            }).ConfigureAwait(false);

        await UnwindAsync(
            tx, batch.Configures, size, cancellationToken,
            """
            UNWIND $rows AS row
            MATCH (ch:Chunk {chunk_id: row.chunk_id})
            MERGE (e:Endpoint {collection: $collection, path: row.path})
            MERGE (ch)-[:CONFIGURES]->(e)
            """,
            rows => new
            {
                collection,
                rows = rows.Select(r => new { chunk_id = r.ChunkId, path = r.Path }).ToArray(),
            }).ConfigureAwait(false);

        await UnwindAsync(
            tx, batch.BuildDeps, size, cancellationToken,
            """
            UNWIND $rows AS row
            MATCH (col:Collection {name: $collection})
            MERGE (a:Artifact {key: row.key})
            SET a.name = row.name,
                a.group = row.group,
                a.ecosystem = row.ecosystem,
                a.version = row.version,
                a.scope = row.scope
            MERGE (col)-[:BUILD_DEPENDS]->(a)
            """,
            rows => new
            {
                collection,
                rows = rows.Select(r => new
                {
                    key = r.Key,
                    name = r.Name,
                    group = r.Group,
                    ecosystem = r.Ecosystem,
                    version = r.Version,
                    scope = r.Scope,
                }).ToArray(),
            }).ConfigureAwait(false);

        await UnwindAsync(
            tx, batch.ResolvesTo, size, cancellationToken,
            """
            UNWIND $rows AS row
            MATCH (a:Artifact {key: row.artifact_key})
            MERGE (col:Collection {name: row.target_collection})
            MERGE (a)-[:RESOLVES_TO]->(col)
            """,
            rows => new
            {
                rows = rows.Select(r => new
                {
                    artifact_key = r.ArtifactKey,
                    target_collection = r.TargetCollection,
                }).ToArray(),
            }).ConfigureAwait(false);
    }

    private static async Task UnwindAsync<T>(
        IAsyncQueryRunner tx,
        IReadOnlyList<T> items,
        int batchSize,
        CancellationToken cancellationToken,
        string cypher,
        Func<IReadOnlyList<T>, object> parameters)
    {
        for (var offset = 0; offset < items.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(batchSize, items.Count - offset);
            var slice = items.Skip(offset).Take(count).ToArray();
            await tx.RunAsync(cypher, parameters(slice)).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, object?> ToPropDict(IReadOnlyDictionary<string, object> props) =>
        props.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);

    private static string? NodeKey(IReadOnlyDictionary<string, object?> props)
    {
        foreach (var field in NodeKeyFields)
        {
            if (props.TryGetValue(field, out var value) && value is not null)
            {
                var s = value.ToString();
                if (!string.IsNullOrEmpty(s))
                {
                    return s;
                }
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, object?> FilterNodeProps(IReadOnlyDictionary<string, object?> props)
    {
        var filtered = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in NodePropKeys)
        {
            if (props.TryGetValue(key, out var value) && value is not null)
            {
                filtered[key] = value;
            }
        }

        return filtered;
    }
}
