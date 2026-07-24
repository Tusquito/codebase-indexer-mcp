using CodebaseIndexer.Infrastructure.Embedding;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Tests for fastembed / Hugging Face hub sparse model cache resolution.</summary>
public sealed class SparseModelCacheResolverTests
{
    [Fact]
    public void Resolve_direct_org_model_path()
    {
        var root = CreateTempDir();
        try
        {
            var modelDir = Path.Combine(root, "Qdrant", "bm25");
            Directory.CreateDirectory(modelDir);
            File.WriteAllText(Path.Combine(modelDir, "english.txt"), "the\n");

            var resolved = SparseModelCacheResolver.ResolveModelDirectory(root, "Qdrant/bm25");
            Assert.Equal(modelDir, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_hf_hub_models_dash_layout()
    {
        var root = CreateTempDir();
        try
        {
            var hubRoot = Path.Combine(root, "models--Qdrant--bm25");
            Directory.CreateDirectory(hubRoot);
            File.WriteAllText(Path.Combine(hubRoot, "english.txt"), "the\n");

            var resolved = SparseModelCacheResolver.ResolveModelDirectory(root, "Qdrant/bm25");
            Assert.Equal(hubRoot, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_hf_hub_snapshot_subdirectory()
    {
        var root = CreateTempDir();
        try
        {
            var snapshot = Path.Combine(root, "models--Qdrant--bm25", "snapshots", "abc123");
            Directory.CreateDirectory(snapshot);
            File.WriteAllText(Path.Combine(snapshot, "english.txt"), "the\n");

            var resolved = SparseModelCacheResolver.ResolveModelDirectory(root, "Qdrant/bm25");
            Assert.Equal(snapshot, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_throws_when_missing()
    {
        var root = CreateTempDir();
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => SparseModelCacheResolver.ResolveModelDirectory(root, "Qdrant/bm25"));
            Assert.Contains("Qdrant/bm25", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "sparse-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
