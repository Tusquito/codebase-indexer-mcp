using CodebaseIndexer.Domain.Ports;

namespace CodebaseIndexer.Application.Services;

/// <summary>Application-layer indexing pipeline service.</summary>
public interface IIndexCodebaseService : IIndexPipeline;
