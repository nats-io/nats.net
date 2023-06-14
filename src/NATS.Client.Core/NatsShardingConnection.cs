using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core;

public readonly struct ShardringNatsCommand
{
    private readonly NatsConnection _connection;
    private readonly string _subject;

    public ShardringNatsCommand(NatsConnection connection, string subject)
    {
        _connection = connection;
        _subject = subject;
    }

    public NatsConnection GetConnection() => _connection;

    public IObservable<T> AsObservable<T>() => _connection.AsObservable<T>(_subject);

    public ValueTask FlushAsync() => _connection.FlushAsync();

    public ValueTask<TimeSpan> PingAsync() => _connection.PingAsync();

    public ValueTask PublishAsync() => _connection.PublishAsync(_subject);

    public ValueTask PublishAsync(byte[] value) => _connection.PublishAsync(_subject, value);

    public ValueTask PublishAsync(ReadOnlyMemory<byte> value) => _connection.PublishAsync(_subject, value);

    public ValueTask PublishAsync<T>(T value) => _connection.PublishAsync(_subject, value);

    public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request) => _connection.RequestAsync<TRequest, TResponse>(_subject, request);

    public Task<NatsReplyHandle> ReplyAsync<TRequest, TResponse>(Func<TRequest, TResponse> reply) => _connection.ReplyAsync<TRequest, TResponse>(_subject, reply);

    public ValueTask<NatsSub<T>> SubscribeAsync<T>() => _connection.SubscribeAsync<T>(_subject);

    public ValueTask<NatsSub> SubscribeAsync(string subject, in NatsSubOpts? opts = default, CancellationToken cancellationToken = default) =>
        _connection.SubscribeAsync(subject, opts, cancellationToken);
}

public sealed class NatsShardingConnection : IAsyncDisposable
{
    private readonly NatsConnectionPool[] _pools;

    public NatsShardingConnection(int poolSize, NatsOptions options, string[] urls)
        : this(poolSize, options, urls, _ => { })
    {
    }

    public NatsShardingConnection(int poolSize, NatsOptions options, string[] urls, Action<NatsConnection> configureConnection)
    {
        poolSize = Math.Max(1, poolSize);
        _pools = new NatsConnectionPool[urls.Length];
        for (var i = 0; i < urls.Length; i++)
        {
            _pools[i] = new NatsConnectionPool(poolSize, options with { Url = urls[i] }, configureConnection);
        }
    }

    public IEnumerable<NatsConnection> GetConnections()
    {
        foreach (var item in _pools)
        {
            foreach (var conn in item.GetConnections())
            {
                yield return conn;
            }
        }
    }

    public ShardringNatsCommand GetCommand(string subject)
    {
        Validate(subject);
        var i = GetHashIndex(subject);
        var pool = _pools[i];
        return new ShardringNatsCommand(pool.GetConnection(), subject);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in _pools)
        {
            await item.DisposeAsync().ConfigureAwait(false);
        }
    }

    // TODO: Unsafe really needed?
    // Benchmark without SkipLocalsInit to see if the performance impact is negligible
    [SkipLocalsInit]
    private int GetHashIndex(string key)
    {
        var source = System.Runtime.InteropServices.MemoryMarshal.AsBytes(key.AsSpan());
        Span<byte> destination = stackalloc byte[4];
        XxHash32.TryHash(source, destination, out var _);

        var hash = BinaryPrimitives.ReadUInt32BigEndian(destination); // xxhash spec is big-endian
        var v = hash % (uint)_pools.Length;
        return (int)v;
    }

    private void Validate(string key)
    {
        if (key.AsSpan().IndexOfAny('*', '>') != -1)
        {
            throw new ArgumentException($"Wild card is not supported in sharding connection. Key:{key}");
        }
    }
}
