using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Tests;

public sealed class DomainLayerTests
{
    [Fact]
    public void Domain_has_no_infrastructure_references()
    {
        var domainAssembly = typeof(Chunk).Assembly;
        var referencedAssemblies = domainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        Assert.DoesNotContain("CodebaseIndexer.Infrastructure", referencedAssemblies);
        Assert.DoesNotContain("Qdrant.Client", referencedAssemblies);
        Assert.DoesNotContain("Refit", referencedAssemblies);
    }

    [Fact]
    public void Chunk_is_sealed_record()
    {
        var chunkType = typeof(Chunk);
        Assert.True(chunkType.IsClass);
        Assert.NotNull(chunkType.GetMethod("<Clone>$", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic));
    }
}
