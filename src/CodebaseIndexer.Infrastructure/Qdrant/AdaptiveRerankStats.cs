namespace CodebaseIndexer.Infrastructure.Qdrant;

/// <summary>Counters for adaptive ColBERT skip decisions (per store instance).</summary>
public sealed class AdaptiveRerankStats
{
    /// <summary>Total adaptive probe evaluations.</summary>
    public int Total { get; set; }

    /// <summary>Times ColBERT MAX_SIM was skipped due to RRF gap.</summary>
    public int Skipped { get; set; }

    /// <summary>Times ColBERT MAX_SIM ran after adaptive probe.</summary>
    public int Reranked { get; set; }

    /// <summary>Resets all counters.</summary>
    public void Reset()
    {
        Total = 0;
        Skipped = 0;
        Reranked = 0;
    }
}
