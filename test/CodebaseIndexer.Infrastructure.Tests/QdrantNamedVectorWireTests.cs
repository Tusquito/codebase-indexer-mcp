using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Serialization;
using CodebaseIndexer.Infrastructure.Qdrant;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>NamedVector ↔ Qdrant wire mapping surface for ADR 0032 Phase 2.</summary>
public sealed class QdrantNamedVectorWireTests
{
    /// <summary>DomainEnumWire matches expected Qdrant named-vector names.</summary>
    [Test]
    [Arguments(NamedVector.Dense, "dense")]
    [Arguments(NamedVector.Sparse, "sparse")]
    [Arguments(NamedVector.Colbert, "colbert")]
    public async Task DomainEnumWire_matches_qdrant_named_vector_name(NamedVector vector, string expected)
    {
        await Assert.That(DomainEnumWire.ToWire(vector)).IsEqualTo(expected);
    }

    /// <summary>QdrantVectorStore create/upsert key set uses the same DomainEnumWire map.</summary>
    [Test]
    public async Task GetNamedVectorWireMap_matches_DomainEnumWire()
    {
        var map = QdrantVectorStore.GetNamedVectorWireMap();
        await Assert.That(map.Count).IsEqualTo(Enum.GetValues<NamedVector>().Length);
        foreach (NamedVector value in Enum.GetValues<NamedVector>())
        {
            await Assert.That(map.TryGetValue(value, out var wire)).IsTrue();
            await Assert.That(wire).IsEqualTo(DomainEnumWire.ToWire(value));
        }
    }
}