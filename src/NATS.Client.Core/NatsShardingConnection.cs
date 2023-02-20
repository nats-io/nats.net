using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.CompilerServices;

namespace NATS.Client.Core;

public sealed class NatsShardingConnection : IAsyncDisposable
{
    readonly NatsConnectionPool[] pools;

    public NatsShardingConnection(int poolSize, NatsOptions options, string[] urls)
        : this(poolSize, options, urls, _ => { })
    {
    }

    public NatsShardingConnection(int poolSize, NatsOptions options, string[] urls, Action<NatsConnection> configureConnection)
    {
        poolSize = Math.Max(1, poolSize);
        pools = new NatsConnectionPool[urls.Length];
        for (int i = 0; i < urls.Length; i++)
        {
            pools[i] = new NatsConnectionPool(poolSize, options with { Url = urls[i] }, configureConnection);
        }
    }

    public IEnumerable<NatsConnection> GetConnections()
    {
        foreach (var item in pools)
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
        var pool = pools[i];
        return new ShardringNatsCommand(pool.GetConnection(), key);
    }

    public ShardringNatsCommand GetCommand(string key)
    {
        Validate(key);
        var i = GetHashIndex(key);
        var pool = pools[i];
        return new ShardringNatsCommand(pool.GetConnection(), new NatsKey(key, true));
    }

    [SkipLocalsInit]
    int GetHashIndex(string key)
    {
        var source = System.Runtime.InteropServices.MemoryMarshal.AsBytes(key.AsSpan());
        Span<byte> destination = stackalloc byte[4];
        XxHash32.TryHash(source, destination, out var _);

        var hash = BinaryPrimitives.ReadUInt32BigEndian(destination); // xxhash spec is big-endian
        var v = hash % (uint)pools.Length;
        return (int)v;
    }

    void Validate(string key)
    {
        if (key.AsSpan().IndexOfAny('*', '>') != -1)
        {
            throw new ArgumentException($"Wild card is not supported in sharding connection. Key:{key}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in pools)
        {
            await item.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public readonly struct ShardringNatsCommand
{
    readonly NatsConnection connection;
    readonly NatsKey key;

    public ShardringNatsCommand(NatsConnection connection, NatsKey key)
    {
        this.connection = connection;
        this.key = key;
    }

    public NatsConnection GetConnection() => connection;

    public IObservable<T> AsObservable<T>() => connection.AsObservable<T>(key);
    public ValueTask FlushAsync() => connection.FlushAsync();
    public ValueTask<TimeSpan> PingAsync() => connection.PingAsync();
    public void PostPing() => connection.PostPing();
    public void PostPublish() => connection.PostPublish(key);
    public void PostPublish(byte[] value) => connection.PostPublish(key, value);
    public void PostPublish(ReadOnlyMemory<byte> value) => connection.PostPublish(key, value);
    public void PostPublish<T>(T value) => connection.PostPublish(key, value);
    public ValueTask PublishAsync() => connection.PublishAsync(key);
    public ValueTask PublishAsync(byte[] value) => connection.PublishAsync(key, value);
    public ValueTask PublishAsync(ReadOnlyMemory<byte> value) => connection.PublishAsync(key, value);
    public ValueTask PublishAsync<T>(T value) => connection.PublishAsync(key, value);
    public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey queueGroup, Action<T> handler) => connection.QueueSubscribeAsync(key, queueGroup, handler);
    public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey queueGroup, Func<T, Task> asyncHandler) => connection.QueueSubscribeAsync(key, queueGroup, asyncHandler);
    public ValueTask<IDisposable> QueueSubscribeAsync<T>(string queueGroup, Action<T> handler) => connection.QueueSubscribeAsync(key.Key, queueGroup, handler);
    public ValueTask<IDisposable> QueueSubscribeAsync<T>(string queueGroup, Func<T, Task> asyncHandler) => connection.QueueSubscribeAsync(key.Key, queueGroup, asyncHandler);
    public ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(TRequest request) => connection.RequestAsync<TRequest, TResponse>(key, request);
    public ValueTask<IDisposable> SubscribeAsync(Action handler) => connection.SubscribeAsync(key, handler);
    public ValueTask<IDisposable> SubscribeAsync<T>(Action<T> handler) => connection.SubscribeAsync<T>(key, handler);
    public ValueTask<IDisposable> SubscribeAsync<T>(Func<T, Task> asyncHandler) => connection.SubscribeAsync<T>(key, asyncHandler);
    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(Func<TRequest, Task<TResponse>> requestHandler) => connection.SubscribeRequestAsync<TRequest, TResponse>(key, requestHandler);
    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(Func<TRequest, TResponse> requestHandler) => connection.SubscribeRequestAsync<TRequest, TResponse>(key, requestHandler);
}
