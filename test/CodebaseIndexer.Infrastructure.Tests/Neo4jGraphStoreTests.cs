using CodebaseIndexer.Application.Options;
using CodebaseIndexer.Domain.Models;
using CodebaseIndexer.Infrastructure.Neo4j;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;
using System.Threading.Tasks;

namespace CodebaseIndexer.Infrastructure.Tests;

/// <summary>Neo4jGraphStore unit tests with a recording fake driver.</summary>
public sealed class Neo4jGraphStoreTests
{
    [Test]
    public async Task Disabled_store_skips_schema_and_writes()
    {
        var driver = new FakeDriver();
        var store = CreateStore(driver, enabled: false);

        await store.EnsureSchemaAsync();
        await store.WriteBatchAsync(new GraphBatch("demo"));

        await Assert.That(driver.Session.Queries).IsEmpty();
        await Assert.That(await store.IsEnabledAsync()).IsFalse();
    }

    [Test]
    public async Task Ensure_schema_runs_constraints()
    {
        var driver = new FakeDriver();
        var store = CreateStore(driver, enabled: true);

        await store.EnsureSchemaAsync();

        await Assert.That(driver.Session.Queries.Count >= 7).IsTrue();
        await Assert.That(driver.Session.Queries).Contains(q => q.Query.Contains("chunk_id_unique", StringComparison.Ordinal));
        await Assert.That(driver.Session.Queries).Contains(q => q.Query.Contains("calls_call_token", StringComparison.Ordinal));
    }

    [Test]
    public async Task Expand_clamps_hops_into_cypher()
    {
        var driver = new FakeDriver();
        var store = CreateStore(driver, enabled: true, maxHops: 2);

        await store.ExpandSubgraphAsync(["c1"], maxHops: 99, maxNodes: 50);

        await Assert.That(driver.Session.Queries).Contains(q => q.Query.Contains("[*1..2]-", StringComparison.Ordinal));
        await Assert.That(driver.Session.Queries).DoesNotContain(q => q.Query.Contains("[*1..99]-", StringComparison.Ordinal));
    }

    [Test]
    public async Task Find_callers_runs_call_token_query()
    {
        var driver = new FakeDriver
        {
            Session =
            {
                CallerRecords =
                [
                    new Dictionary<string, object?>
                    {
                        ["chunk_id"] = "chunk-caller",
                        ["rel_path"] = "CreateTie.java",
                        ["start_line"] = 3,
                        ["end_line"] = 7,
                        ["language"] = "java",
                        ["symbol_name"] = "createTie",
                        ["symbol_type"] = "method",
                    },
                ],
            },
        };
        var store = CreateStore(driver, enabled: true);

        var hits = await store.FindCallersAsync("createTie", ["myproj"], receiver: null, limitPerCollection: 10);

        await Assert.That(hits.IsSuccess).IsTrue();
        await Assert.That(hits.Value).HasSingleItem();
        await Assert.That(hits.Value[0].Id.Value).IsEqualTo("chunk-caller");
        await Assert.That(driver.Session.Queries).Contains(q => q.Query.Contains("r.call_token", StringComparison.Ordinal));
    }

    [Test]
    public async Task Write_batch_runs_unwind_statements()
    {
        var driver = new FakeDriver();
        var store = CreateStore(driver, enabled: true);
        var batch = new GraphBatch("demo");
        batch.Files.Add(new GraphFileRow("a.py", SourceLanguage.Python, "sha"));
        batch.Chunks.Add(new GraphChunkRow("c1", "a.py", 1, 2));

        await store.WriteBatchAsync(batch);

        await Assert.That(driver.Session.Queries).Contains(q => q.Query.Contains("UNWIND $files", StringComparison.Ordinal));
        await Assert.That(driver.Session.Queries).Contains(q => q.Query.Contains("UNWIND $chunks", StringComparison.Ordinal));
    }

    private static Neo4jGraphStore CreateStore(FakeDriver driver, bool enabled, int maxHops = 2) =>
        new(
            driver,
            Options.Create(new GraphOptions
            {
                Enabled = enabled,
                Neo4jUri = "bolt://localhost:7687",
                Neo4jUser = "neo4j",
                Neo4jPassword = "pw",
                Neo4jDatabase = "neo4j",
                WriterBatch = 500,
                MaxHops = maxHops,
                MaxNodes = 200,
            }),
            NullLogger<Neo4jGraphStore>.Instance);

    private sealed class FakeDriver : IDriver
    {
        public FakeSession Session { get; set; } = new();

        public bool Encrypted => false;
        public Config Config { get; } = new();

        public IAsyncSession AsyncSession() => Session;
        public IAsyncSession AsyncSession(Action<SessionConfigBuilder> action) => Session;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task CloseAsync() => Task.CompletedTask;
        public Task VerifyConnectivityAsync() => Task.CompletedTask;
        public Task<bool> SupportsMultiDbAsync() => Task.FromResult(true);
        public Task<bool> SupportsSessionAuthAsync() => Task.FromResult(false);
        public Task<IServerInfo> GetServerInfoAsync() => throw new NotSupportedException();
        public IExecutableQuery<IRecord, IRecord> ExecutableQuery(string query) => throw new NotSupportedException();
        public Task<bool> TryVerifyConnectivityAsync() => Task.FromResult(true);
        public Task<bool> VerifyAuthenticationAsync(IAuthToken authToken) => Task.FromResult(true);
    }

    private sealed class FakeSession : IAsyncSession
    {
        public List<(string Query, object? Parameters)> Queries { get; } = [];
        public List<IReadOnlyDictionary<string, object?>> CallerRecords { get; set; } = [];

#pragma warning disable CS0618 // Bookmark replaced by Bookmarks; still required by IAsyncSession
        public Bookmark LastBookmark => Bookmark.From((string[]?)null);
#pragma warning restore CS0618
        public Bookmarks LastBookmarks => Bookmarks.From((string[]?)null);
        public SessionConfig SessionConfig { get; } =
            (SessionConfig)Activator.CreateInstance(typeof(SessionConfig), nonPublic: true)!;

        public Task CloseAsync() => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<T> ExecuteReadAsync<T>(Func<IAsyncQueryRunner, Task<T>> work, Action<TransactionConfigBuilder>? action = null) =>
            work(new FakeQueryRunner(this));

        public Task ExecuteReadAsync(Func<IAsyncQueryRunner, Task> work, Action<TransactionConfigBuilder>? action = null) =>
            work(new FakeQueryRunner(this));

        public Task<T> ExecuteWriteAsync<T>(Func<IAsyncQueryRunner, Task<T>> work, Action<TransactionConfigBuilder>? action = null) =>
            work(new FakeQueryRunner(this));

        public Task ExecuteWriteAsync(Func<IAsyncQueryRunner, Task> work, Action<TransactionConfigBuilder>? action = null) =>
            work(new FakeQueryRunner(this));

        public Task<IResultCursor> RunAsync(string query) => RunAsync(query, (object?)null);

        public Task<IResultCursor> RunAsync(string query, object? parameters)
        {
            Queries.Add((query, parameters));
            return Task.FromResult<IResultCursor>(new FakeResultCursor(this, query));
        }

        public Task<IResultCursor> RunAsync(string query, IDictionary<string, object> parameters) =>
            RunAsync(query, (object)parameters);

        public Task<IResultCursor> RunAsync(Query query) => RunAsync(query.Text, query.Parameters);

        public Task<IResultCursor> RunAsync(string query, Action<TransactionConfigBuilder> action) =>
            RunAsync(query, (object?)null);

        public Task<IResultCursor> RunAsync(string query, IDictionary<string, object> parameters, Action<TransactionConfigBuilder> action) =>
            RunAsync(query, parameters);

        public Task<IResultCursor> RunAsync(Query query, Action<TransactionConfigBuilder> action) =>
            RunAsync(query);

        public Task<IAsyncTransaction> BeginTransactionAsync() => throw new NotSupportedException();
        public Task<IAsyncTransaction> BeginTransactionAsync(Action<TransactionConfigBuilder> action) =>
            throw new NotSupportedException();
        public Task<T> ReadTransactionAsync<T>(Func<IAsyncTransaction, Task<T>> work, Action<TransactionConfigBuilder>? action = null) =>
            throw new NotSupportedException();
        public Task ReadTransactionAsync(Func<IAsyncTransaction, Task> work, Action<TransactionConfigBuilder>? action = null) =>
            throw new NotSupportedException();
        public Task<T> WriteTransactionAsync<T>(Func<IAsyncTransaction, Task<T>> work, Action<TransactionConfigBuilder>? action = null) =>
            throw new NotSupportedException();
        public Task WriteTransactionAsync(Func<IAsyncTransaction, Task> work, Action<TransactionConfigBuilder>? action = null) =>
            throw new NotSupportedException();
    }

    private sealed class FakeQueryRunner(FakeSession session) : IAsyncQueryRunner
    {
        public Task<IResultCursor> RunAsync(string query) => session.RunAsync(query);
        public Task<IResultCursor> RunAsync(string query, object parameters) => session.RunAsync(query, parameters);
        public Task<IResultCursor> RunAsync(string query, IDictionary<string, object> parameters) =>
            session.RunAsync(query, parameters);
        public Task<IResultCursor> RunAsync(Query query) => session.RunAsync(query);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeResultCursor(FakeSession session, string query) : IResultCursor
    {
        private int _index;
        private readonly List<IRecord> _records = query.Contains("r.call_token", StringComparison.Ordinal)
            ? session.CallerRecords.Select(r => (IRecord)new FakeRecord(r)).ToList()
            : [];

        public IRecord Current => _records[Math.Max(0, _index - 1)];
        public bool IsOpen => true;

        public Task<string[]> KeysAsync() => Task.FromResult(Array.Empty<string>());
        public Task<IResultSummary> ConsumeAsync() => throw new NotSupportedException();
        public Task<IRecord> PeekAsync() => Task.FromResult(Current);

        public Task<bool> FetchAsync()
        {
            if (_index >= _records.Count)
            {
                return Task.FromResult(false);
            }

            _index++;
            return Task.FromResult(true);
        }

        public async IAsyncEnumerator<IRecord> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            foreach (var record in _records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return record;
                await Task.Yield();
            }
        }
    }

    private sealed class FakeRecord : IRecord
    {
        private readonly Dictionary<string, object> _values;

        public FakeRecord(IReadOnlyDictionary<string, object?> values) =>
            _values = values.ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);

        public object this[int index] => _values.Values.ElementAt(index);
        public object this[string key] => _values[key];
        public IReadOnlyDictionary<string, object> Values => _values;
        public IReadOnlyList<string> Keys => _values.Keys.ToArray();
        public int Count => _values.Count;

        IEnumerable<string> IReadOnlyDictionary<string, object>.Keys => _values.Keys;
        IEnumerable<object> IReadOnlyDictionary<string, object>.Values => _values.Values;

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public bool TryGetValue(string key, out object value) => _values.TryGetValue(key, out value!);

        public T Get<T>(string key) => (T)_values[key];

        public bool TryGet<T>(string key, out T value)
        {
            if (_values.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default!;
            return false;
        }

        public T GetCaseInsensitive<T>(string key) =>
            Get<T>(_values.Keys.First(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)));

        public bool TryGetCaseInsensitive<T>(string key, out T value)
        {
            var match = _values.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                value = default!;
                return false;
            }

            return TryGet(match, out value);
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _values.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}