using System.Text.RegularExpressions;
using CodebaseIndexer.Application.BuildDeps;
using CodebaseIndexer.Application.Search;
using CodebaseIndexer.Domain.Models;
using Microsoft.Extensions.Logging;

namespace CodebaseIndexer.Application.Graph;

/// <summary>Index-time Neo4j graph batch builder (Python <c>graph_writer.py</c>).</summary>
public sealed partial class GraphWriter
{
    private readonly ILogger<GraphWriter> _logger;

    /// <summary>Creates the graph writer.</summary>
    public GraphWriter(ILogger<GraphWriter> logger) => _logger = logger;

    /// <summary>Stable Symbol key: {collection}:{rel_path}::{symbol_name}.</summary>
    public static string SymbolQualifiedName(string collection, string relPath, string symbolName) =>
        $"{collection}:{relPath}::{symbolName}";

    /// <summary>Qualified name for an unresolved import target.</summary>
    public static string ImportQualifiedName(string collection, string importName) =>
        $"{collection}::import::{importName}";

    /// <summary>Qualified name for a best-effort callee symbol.</summary>
    public static string CalleeQualifiedName(string collection, string callee) =>
        $"{collection}::callee::{callee}";

    /// <summary>Stable Artifact key across collections.</summary>
    public static string ArtifactKey(string group, string name, string ecosystem) =>
        string.IsNullOrEmpty(group) ? $"{ecosystem}:{name}" : $"{ecosystem}:{group}:{name}";

    /// <summary>Resolve CALLS target symbol (qualified_name, display name) — ADR 0023 Rules 1–3.</summary>
    public static (string QualifiedName, string Name) ResolveCallTarget(
        string callToken,
        string collection,
        string relPath,
        IReadOnlyDictionary<string, List<GraphDefineEntry>> definesByName,
        IReadOnlyList<string> fileImports)
    {
        _ = relPath;

        if (definesByName.TryGetValue(callToken, out var exact) && exact.Count == 1)
        {
            var e = exact[0];
            return (e.QualifiedName, e.Name);
        }

        if (!callToken.Contains('.', StringComparison.Ordinal))
        {
            return (CalleeQualifiedName(collection, callToken), callToken);
        }

        var dot = callToken.LastIndexOf('.');
        var receiver = callToken[..dot];
        var method = callToken[(dot + 1)..];

        if (definesByName.TryGetValue(method, out var byMethod) && byMethod.Count == 1)
        {
            var e = byMethod[0];
            return (e.QualifiedName, e.Name);
        }

        var matchingImports = fileImports
            .Where(imp => string.Equals(imp, receiver, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingImports.Length > 0 && definesByName.TryGetValue(method, out var methodEntries))
        {
            var filtered = methodEntries
                .Where(e => matchingImports.Any(imp =>
                    e.RelPath.Contains(imp, StringComparison.OrdinalIgnoreCase)
                    || e.QualifiedName.Contains(imp, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (filtered.Length == 1)
            {
                return (filtered[0].QualifiedName, filtered[0].Name);
            }
        }

        return (CalleeQualifiedName(collection, callToken), callToken);
    }

    /// <summary>Best-effort HTTP method inference from endpoint definition content.</summary>
    public static string InferHttpMethod(string content)
    {
        var match = HttpMethodPattern().Match(content);
        if (!match.Success)
        {
            return string.Empty;
        }

        var token = match.Value.ToLowerInvariant();
        foreach (var method in new[] { "get", "post", "put", "delete", "patch" })
        {
            if (token.Contains(method, StringComparison.Ordinal))
            {
                return method.ToUpperInvariant();
            }
        }

        return string.Empty;
    }

    /// <summary>Build a graph batch from indexed chunks (grouped by file).</summary>
    public GraphBatch BuildGraphBatch(
        string collection,
        IReadOnlyList<Chunk> chunks,
        UrlExtractors urlExtractors,
        string workspacePath,
        IReadOnlyList<string> collectionNames)
    {
        var batch = new GraphBatch(collection);
        if (chunks.Count == 0)
        {
            return batch;
        }

        var byFile = chunks.GroupBy(c => c.RelPath, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Chunk>)g.ToArray(), StringComparer.Ordinal);

        var definesByName = new Dictionary<string, List<GraphDefineEntry>>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrEmpty(chunk.SymbolName))
            {
                continue;
            }

            var qn = SymbolQualifiedName(collection, chunk.RelPath, chunk.SymbolName);
            if (!definesByName.TryGetValue(chunk.SymbolName, out var list))
            {
                list = [];
                definesByName[chunk.SymbolName] = list;
            }

            list.Add(new GraphDefineEntry(qn, chunk.RelPath, chunk.SymbolName, chunk.SymbolType));
        }

        var importsByFile = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        var seenBuildKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (relPath, fileChunks) in byFile)
        {
            var first = fileChunks[0];
            if (seenFiles.Add(relPath))
            {
                batch.Files.Add(new GraphFileRow(relPath, first.Language, first.FileSha256));
            }

            foreach (var chunk in fileChunks)
            {
                batch.Chunks.Add(new GraphChunkRow(chunk.Id.Value, relPath, chunk.StartLine, chunk.EndLine));

                if (!string.IsNullOrEmpty(chunk.SymbolName))
                {
                    var qn = SymbolQualifiedName(collection, relPath, chunk.SymbolName);
                    batch.Defines.Add(new GraphDefineRow(chunk.Id.Value, qn, chunk.SymbolName, chunk.SymbolType));
                }

                foreach (var callee in chunk.Callees)
                {
                    if (!importsByFile.ContainsKey(relPath))
                    {
                        var fileContent = ReadFileContent(workspacePath, relPath)
                            ?? string.Join('\n', fileChunks.Select(c => c.Content));
                        importsByFile[relPath] = ImportNameExtractor.ExtractFileImportNames(fileContent, first.Language);
                    }

                    var (qn, symName) = ResolveCallTarget(
                        callee, collection, relPath, definesByName, importsByFile[relPath]);
                    batch.Calls.Add(new GraphCallRow(chunk.Id.Value, qn, symName, callee));
                }

                foreach (var route in UrlExtractors.RoutePaths(chunk.Content, relPath))
                {
                    var method = InferHttpMethod(chunk.Content);
                    batch.Endpoints.Add(new GraphEndpointRow(route, method));
                    batch.DeclaresEndpoint.Add(new GraphDeclaresEndpointRow(chunk.Id.Value, route));
                }

                foreach (var path in urlExtractors.CodeUrls(chunk.Content))
                {
                    batch.Endpoints.Add(new GraphEndpointRow(path, string.Empty));
                    batch.HttpCalls.Add(new GraphHttpCallRow(chunk.Id.Value, path));
                }

                if (ConfigFilePattern().IsMatch(relPath))
                {
                    var (configPaths, _) = urlExtractors.ConfigUrls(chunk.Content);
                    foreach (var path in configPaths)
                    {
                        batch.Endpoints.Add(new GraphEndpointRow(path, string.Empty));
                        batch.Configures.Add(new GraphConfiguresRow(chunk.Id.Value, path));
                    }
                }
            }

            var fullContent = ReadFileContent(workspacePath, relPath)
                ?? string.Join('\n', fileChunks.Select(c => c.Content));
            foreach (var importName in ImportNameExtractor.ExtractFileImportNames(fullContent, first.Language))
            {
                batch.Imports.Add(new GraphImportRow(
                    relPath, ImportQualifiedName(collection, importName), importName));
            }

            if (BuildManifestDetector.IsBuildManifest(relPath))
            {
                var manifestContent = ReadFileContent(workspacePath, relPath) ?? fullContent;
                var deps = BuildDepExtractor.Extract(manifestContent, relPath);
                var matches = BuildDepCollectionMatcher.Match(deps, collectionNames, collection);
                var matchByArtifact = matches
                    .GroupBy(m => m.Artifact, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().MatchedCollection, StringComparer.Ordinal);

                foreach (var dep in deps)
                {
                    var key = ArtifactKey(dep.Group, dep.Artifact, dep.Ecosystem);
                    if (!seenBuildKeys.Add(key))
                    {
                        continue;
                    }

                    batch.BuildDeps.Add(new GraphBuildDepRow(
                        key, dep.Artifact, dep.Group, dep.Ecosystem, dep.Version, dep.Scope));
                    if (matchByArtifact.TryGetValue(dep.Artifact, out var target))
                    {
                        batch.ResolvesTo.Add(new GraphResolvesToRow(key, target));
                    }
                }
            }
        }

        return batch;
    }

    /// <summary>Map each chunk_id to neighbor Neo4j node keys (ADR 0002 Phase 2).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GraphNodeIdsFromBatch(GraphBatch batch)
    {
        var collection = batch.Collection;
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void Add(string chunkId, string key)
        {
            if (!seen.TryGetValue(chunkId, out var bucket))
            {
                bucket = new HashSet<string>(StringComparer.Ordinal);
                seen[chunkId] = bucket;
            }

            if (!bucket.Add(key))
            {
                return;
            }

            if (!result.TryGetValue(chunkId, out var list))
            {
                list = [];
                result[chunkId] = list;
            }

            list.Add(key);
        }

        foreach (var row in batch.Defines)
        {
            Add(row.ChunkId, row.QualifiedName);
        }

        foreach (var row in batch.Calls)
        {
            Add(row.ChunkId, row.QualifiedName);
        }

        foreach (var row in batch.DeclaresEndpoint)
        {
            Add(row.ChunkId, $"{collection}:{row.Path}");
        }

        foreach (var row in batch.HttpCalls)
        {
            Add(row.ChunkId, $"{collection}:{row.Path}");
        }

        foreach (var row in batch.Configures)
        {
            Add(row.ChunkId, $"{collection}:{row.Path}");
        }

        var chunkIdsByFile = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var chunk in batch.Chunks)
        {
            if (!chunkIdsByFile.TryGetValue(chunk.RelPath, out var list))
            {
                list = [];
                chunkIdsByFile[chunk.RelPath] = list;
            }

            list.Add(chunk.ChunkId);
        }

        foreach (var row in batch.Imports)
        {
            if (!chunkIdsByFile.TryGetValue(row.RelPath, out var chunkIds))
            {
                continue;
            }

            foreach (var chunkId in chunkIds)
            {
                Add(chunkId, row.QualifiedName);
            }
        }

        return result.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value,
            StringComparer.Ordinal);
    }

    private string? ReadFileContent(string workspacePath, string relPath)
    {
        var path = Path.Combine(workspacePath, relPath.Replace('\\', '/'));
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "graph_writer_read_failed path={Path}", relPath);
            return null;
        }
    }

    [GeneratedRegex(
        @"@(?:Get|Post|Put|Delete|Patch|Request)Mapping|" +
        @"\[(?:Http(?:Get|Post|Put|Delete|Patch))\]|" +
        @"\.Map(Get|Post|Put|Delete|Patch)\(|" +
        @"(?:app|router)\.(get|post|put|delete|patch)\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpMethodPattern();

    [GeneratedRegex(@"\.(ya?ml|json|properties|env|config)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConfigFilePattern();
}
