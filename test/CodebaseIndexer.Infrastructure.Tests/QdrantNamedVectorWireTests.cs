using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Domain.Serialization;
using CodebaseIndexer.Infrastructure.Qdrant;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>NamedVector ↔ Qdrant wire mapping surface for ADR 0032 Phase 2.</summary>
public sealed class QdrantNamedVectorWireTests
{
    /// <summary>DomainEnumWire matches expected Qdrant named-vector names.</summary>
    [Theory]
    [InlineData(NamedVector.Dense, "dense")]
    [InlineData(NamedVector.Sparse, "sparse")]
    [InlineData(NamedVector.Colbert, "colbert")]
    public void DomainEnumWire_matches_qdrant_named_vector_name(NamedVector vector, string expected)
    {
        Assert.Equal(expected, DomainEnumWire.ToWire(vector));
    }

    /// <summary>QdrantVectorStore create/upsert key set uses the same DomainEnumWire map.</summary>
    [Fact]
    public void GetNamedVectorWireMap_matches_DomainEnumWire()
    {
        var map = QdrantVectorStore.GetNamedVectorWireMap();
        Assert.Equal(Enum.GetValues<NamedVector>().Length, map.Count);
        foreach (NamedVector value in Enum.GetValues<NamedVector>())
        {
            Assert.True(map.TryGetValue(value, out var wire));
            Assert.Equal(DomainEnumWire.ToWire(value), wire);
        }
    }
}
