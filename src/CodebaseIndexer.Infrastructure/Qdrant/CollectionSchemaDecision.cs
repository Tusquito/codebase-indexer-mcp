namespace CodebaseIndexer.Infrastructure.Qdrant;

/// <summary>Outcome of comparing live Qdrant schema to configured embedding options.</summary>
/// <param name="NeedsRecreate">Whether the collection must be dropped and recreated.</param>
/// <param name="ColbertMismatch">ColBERT named vector presence disagrees with rerank config.</param>
/// <param name="ColbertDimMismatch">ColBERT token dimension disagrees with the model registry.</param>
internal readonly record struct CollectionSchemaDecision(
    bool NeedsRecreate,
    bool ColbertMismatch = false,
    bool ColbertDimMismatch = false);
