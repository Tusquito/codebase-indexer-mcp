namespace CodebaseIndexer.Infrastructure.Embedding;

/// <summary>
/// Resolves on-disk sparse model directories under a fastembed / Hugging Face hub cache root.
/// </summary>
internal static class SparseModelCacheResolver
{
    /// <summary>
    /// Finds the model directory for <paramref name="modelName"/> under <paramref name="cacheRoot"/>.
    /// Supports direct <c>org/model</c> paths and HF hub <c>models--org--model[/snapshots/rev]</c> layout.
    /// </summary>
    public static string ResolveModelDirectory(string cacheRoot, string modelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        var normalized = modelName.Replace('/', Path.DirectorySeparatorChar);
        var direct = Path.Combine(cacheRoot, normalized);
        if (Directory.Exists(direct))
        {
            return PreferSnapshotDir(direct);
        }

        // Hugging Face hub cache: models--{id with '/' → '--'} (e.g. models--Qdrant--bm25).
        var hubFolder = ToHfHubModelFolder(modelName);
        var hubRoot = Path.Combine(cacheRoot, hubFolder);
        if (Directory.Exists(hubRoot))
        {
            return PreferSnapshotDir(hubRoot);
        }

        // Legacy / mistaken single-dash form used by some resolvers (models--Qdrant-bm25).
        var hubFolderAlt = "models--" + modelName.Replace('\\', '/').Replace('/', '-');
        if (!string.Equals(hubFolderAlt, hubFolder, StringComparison.Ordinal))
        {
            var hubRootAlt = Path.Combine(cacheRoot, hubFolderAlt);
            if (Directory.Exists(hubRootAlt))
            {
                return PreferSnapshotDir(hubRootAlt);
            }
        }

        if (!Directory.Exists(cacheRoot))
        {
            throw new InvalidOperationException($"Fastembed cache directory '{cacheRoot}' does not exist.");
        }

        var slashForm = modelName.Replace('\\', '/');
        var match = Directory.EnumerateDirectories(cacheRoot, "*", SearchOption.AllDirectories)
            .Select(PreferSnapshotDir)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(d =>
            {
                var unified = d.Replace('\\', '/');
                return unified.EndsWith(slashForm, StringComparison.OrdinalIgnoreCase)
                    || unified.EndsWith("/" + hubFolder, StringComparison.OrdinalIgnoreCase)
                    || unified.Contains("/" + hubFolder + "/", StringComparison.OrdinalIgnoreCase)
                    || unified.EndsWith("/" + hubFolderAlt, StringComparison.OrdinalIgnoreCase)
                    || unified.Contains("/" + hubFolderAlt + "/", StringComparison.OrdinalIgnoreCase);
            });

        if (match is not null)
        {
            return match;
        }

        throw new InvalidOperationException($"Sparse model '{modelName}' not found under '{cacheRoot}'.");
    }

    /// <summary>
    /// When <paramref name="modelRoot"/> is an HF hub model folder with snapshots, return the first snapshot.
    /// </summary>
    internal static string PreferSnapshotDir(string modelRoot)
    {
        var snapshots = Path.Combine(modelRoot, "snapshots");
        if (!Directory.Exists(snapshots))
        {
            return modelRoot;
        }

        var first = Directory.EnumerateDirectories(snapshots).OrderBy(static d => d, StringComparer.Ordinal).FirstOrDefault();
        return first ?? modelRoot;
    }

    /// <summary>HF hub on-disk folder for a model id (<c>org/name</c> → <c>models--org--name</c>).</summary>
    internal static string ToHfHubModelFolder(string modelName) =>
        "models--" + modelName.Replace('\\', '/').Replace("/", "--", StringComparison.Ordinal);
}
