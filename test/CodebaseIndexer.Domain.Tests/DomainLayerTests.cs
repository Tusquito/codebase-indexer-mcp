using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Domain.Tests;

/// <summary>Verifies domain layer structure and model contracts.</summary>
public sealed class DomainLayerTests
{
    /// <summary>Domain assembly must not reference infrastructure packages.</summary>
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
        // ADR 0033: Domain-owned Result — no FluentResults / ErrorOr / OneOf NuGet in Domain.
        Assert.DoesNotContain("FluentResults", referencedAssemblies);
        Assert.DoesNotContain("ErrorOr", referencedAssemblies);
        Assert.DoesNotContain("OneOf", referencedAssemblies);
    }

    /// <summary><see cref="Chunk"/> is a sealed record with clone support.</summary>
    [Fact]
    public void Chunk_is_sealed_record()
    {
        var chunkType = typeof(Chunk);
        Assert.True(chunkType.IsClass);
        Assert.NotNull(chunkType.GetMethod("<Clone>$", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic));
    }
}
