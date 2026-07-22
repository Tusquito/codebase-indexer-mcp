using Neo4j.Driver;

namespace CodebaseIndexer.Host.Tests;

/// <summary>Minimal <see cref="IDriver"/> stub for Neo4j readiness probes.</summary>
internal sealed class StubNeo4jDriver : IDriver
{
    private readonly bool _healthy;

    public StubNeo4jDriver(bool healthy) => _healthy = healthy;

    public bool Encrypted => false;
    public Config Config { get; } = new();

    public IAsyncSession AsyncSession() => throw new NotSupportedException();
    public IAsyncSession AsyncSession(Action<SessionConfigBuilder> action) => throw new NotSupportedException();
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public Task CloseAsync() => Task.CompletedTask;

    public Task VerifyConnectivityAsync()
    {
        if (!_healthy)
        {
            throw new Neo4jException("bolt unreachable");
        }

        return Task.CompletedTask;
    }

    public Task<bool> SupportsMultiDbAsync() => Task.FromResult(true);
    public Task<bool> SupportsSessionAuthAsync() => Task.FromResult(false);
    public Task<IServerInfo> GetServerInfoAsync() => throw new NotSupportedException();
    public IExecutableQuery<IRecord, IRecord> ExecutableQuery(string query) => throw new NotSupportedException();
    public Task<bool> TryVerifyConnectivityAsync() => Task.FromResult(_healthy);
    public Task<bool> VerifyAuthenticationAsync(IAuthToken authToken) => Task.FromResult(_healthy);
}
