using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core;

public readonly struct ShardringNatsCommand
{
    private readonly NatsConnection _connection;
    private readonly NatsKey _key;

    public ShardringNatsCommand(NatsConnection connection, NatsKey key)
    {
        _connection = connection;
        _key = key;
    }

    public NatsConnection GetConnection() => _connection;

    public IObservable<T> AsObservable<T>() => _connection.AsObservable<T>(_key);

    public ValueTask FlushAsync() => _connection.FlushAsync();

    public ValueTask<TimeSpan> PingAsync() => _connection.PingAsync();

    public void PostPing() => _connection.PostPing();

    public void PostPublish() => _connection.PostPublish(_key.Key);

    public void PostPublish(byte[] value) => _connection.PostPublish(_key.Key, value);

    public void PostPublish(ReadOnlyMemory<byte> value) => _connection.PostPublish(_key.Key, value);

    public void PostPublish<T>(T value) => _connection.PostPublish(_key.Key, value);

    public ValueTask PublishAsync() => _connection.PublishAsync(_key.Key);

    public ValueTask PublishAsync(byte[] value) => _connection.PublishAsync(_key.Key, value);

    public ValueTask PublishAsync(ReadOnlyMemory<byte> value) => _connection.PublishAsync(_key.Key, value);

    public ValueTask PublishAsync<T>(T value) => _connection.PublishAsync(_key.Key, value);

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey queueGroup, Action<T> handler) => _connection.QueueSubscribeAsync(_key, queueGroup, handler);

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey queueGroup, Func<T, Task> asyncHandler) => _connection.QueueSubscribeAsync(_key, queueGroup, asyncHandler);

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(string queueGroup, Action<T> handler) => _connection.QueueSubscribeAsync(_key.Key, queueGroup, handler);

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(string queueGroup, Func<T, Task> asyncHandler) => _connection.QueueSubscribeAsync(_key.Key, queueGroup, asyncHandler);

    public ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(TRequest request) => _connection.RequestAsync<TRequest, TResponse>(_key, request);

    public ValueTask<IDisposable> SubscribeAsync(Action handler) => _connection.SubscribeAsync(_key, handler);

    public ValueTask<IDisposable> SubscribeAsync<T>(Action<T> handler) => _connection.SubscribeAsync<T>(_key, handler);

    public ValueTask<IDisposable> SubscribeAsync<T>(Func<T, Task> asyncHandler) => _connection.SubscribeAsync<T>(_key, asyncHandler);

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(Func<TRequest, Task<TResponse>> requestHandler) => _connection.SubscribeRequestAsync<TRequest, TResponse>(_key, requestHandler);

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(Func<TRequest, TResponse> requestHandler) => _connection.SubscribeRequestAsync<TRequest, TResponse>(_key, requestHandler);
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

    public ShardringNatsCommand GetCommand(in NatsKey key)
    {
        Validate(key.Key);
        var i = GetHashIndex(key.Key);
        var pool = _pools[i];
        return new ShardringNatsCommand(pool.GetConnection(), key);
    }

    public ShardringNatsCommand GetCommand(string key)
    {
        Validate(key);
        var i = GetHashIndex(key);
        var pool = _pools[i];
        return new ShardringNatsCommand(pool.GetConnection(), new NatsKey(key, true));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in _pools)
        {
            await item.DisposeAsync().ConfigureAwait(false);
        }
    }

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
