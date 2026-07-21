using CodebaseIndexer.Domain.Models;

namespace CodebaseIndexer.Application.Search;

/// <summary>Cross-collection rank-based RRF fusion (Python <c>fuse_cross_collection_rrf</c>).</summary>
public static class CrossCollectionRrf
{
    /// <summary>
    /// Re-fuses per-collection ranked lists with global RRF.
    /// Score formula: <c>1 / (rrf_k + rank)</c> with deterministic tie-break on (collection, chunk_id).
    /// </summary>
    public static IReadOnlyList<SearchHit> Fuse(
        IReadOnlyList<IReadOnlyList<SearchHit>> perCollectionResults,
        int rrfK,
        int topK)
    {
        var fusedScores = new Dictionary<(string Collection, string ChunkId), double>();
        var byKey = new Dictionary<(string Collection, string ChunkId), SearchHit>();

        foreach (var collResults in perCollectionResults)
        {
            for (var rank = 0; rank < collResults.Count; rank++)
            {
                var result = collResults[rank];
                var key = (result.Collection, result.Id.Value);
                fusedScores[key] = fusedScores.GetValueOrDefault(key) + 1.0 / (rrfK + rank + 1);
                byKey[key] = result;
            }
        }

        return fusedScores
            .OrderByDescending(kv => kv.Value)
            .ThenByDescending(kv => kv.Key.Collection, StringComparer.Ordinal)
            .ThenByDescending(kv => kv.Key.ChunkId, StringComparer.Ordinal)
            .Take(topK)
            .Select(kv => byKey[kv.Key])
            .ToArray();
    }
}
