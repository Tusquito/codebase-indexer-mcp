namespace CodebaseIndexer.Domain.Models;

/// <summary>Sparse embedding vector represented as parallel index and value arrays.</summary>
/// <param name="Indices">Token or dimension indices with non-zero weights.</param>
/// <param name="Values">Non-zero weight values corresponding to <paramref name="Indices"/>.</param>
public sealed record SparseVector(IReadOnlyList<uint> Indices, IReadOnlyList<float> Values)
{
    /// <summary>Token or dimension indices with non-zero weights.</summary>
    public IReadOnlyList<uint> Indices { get; init; } = Indices;

    /// <summary>Non-zero weight values corresponding to <see cref="Indices"/>.</summary>
    public IReadOnlyList<float> Values { get; init; } = Values;
}
