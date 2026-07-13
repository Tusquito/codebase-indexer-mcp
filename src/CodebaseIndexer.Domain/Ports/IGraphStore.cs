namespace CodebaseIndexer.Domain.Ports;

/// <summary>Port for optional code graph storage and querying.</summary>
public interface IGraphStore
{
    /// <summary>Checks whether the graph store integration is enabled and reachable.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns><see langword="true"/> if the graph store is enabled; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default);
}
