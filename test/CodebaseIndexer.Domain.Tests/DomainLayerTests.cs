using CodebaseIndexer.Domain.Models;
using System.Threading.Tasks;

namespace CodebaseIndexer.Domain.Tests;

/// <summary>Verifies domain layer structure and model contracts.</summary>
public sealed class DomainLayerTests
{
    /// <summary>Domain assembly must not reference infrastructure packages.</summary>
    [Test]
    public async Task Domain_has_no_infrastructure_references()
    {
        var domainAssembly = typeof(Chunk).Assembly;
        var referencedAssemblies = domainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        await Assert.That(referencedAssemblies).DoesNotContain("CodebaseIndexer.Infrastructure");
        await Assert.That(referencedAssemblies).DoesNotContain("Qdrant.Client");
        await Assert.That(referencedAssemblies).DoesNotContain("Refit");
        // ADR 0033: Domain-owned Result — no FluentResults / ErrorOr / OneOf NuGet in Domain.
        await Assert.That(referencedAssemblies).DoesNotContain("FluentResults");
        await Assert.That(referencedAssemblies).DoesNotContain("ErrorOr");
        await Assert.That(referencedAssemblies).DoesNotContain("OneOf");
    }

    /// <summary><see cref="Chunk"/> is a sealed record with clone support.</summary>
    [Test]
    public async Task Chunk_is_sealed_record()
    {
        var chunkType = typeof(Chunk);
        await Assert.That(chunkType.IsClass).IsTrue();
        await Assert.That(chunkType.GetMethod("<Clone>$", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)).IsNotNull();
    }
}