namespace NATS.Client.Core;

public sealed class NatsConnectionPool : INatsConnectionPool
{
    private readonly NatsConnection[] _connections;
    private int _index = -1;

    public NatsConnectionPool()
        : this(Environment.ProcessorCount / 2, NatsOpts.Default, _ => { })
    {
    }

    public NatsConnectionPool(int poolSize)
        : this(poolSize, NatsOpts.Default, _ => { })
    {
    }

    public NatsConnectionPool(NatsOpts opts)
        : this(Environment.ProcessorCount / 2, opts, _ => { })
    {
    }

    public NatsConnectionPool(int poolSize, NatsOpts opts)
        : this(poolSize, opts, _ => { })
    {
    }

    public NatsConnectionPool(int poolSize, NatsOpts opts, Action<NatsConnection> configureConnection)
    {
        poolSize = Math.Max(1, poolSize);
        _connections = new NatsConnection[poolSize];
        for (var i = 0; i < _connections.Length; i++)
        {
            var name = (opts.Name == null) ? $"#{i}" : $"{opts.Name}#{i}";
            var conn = new NatsConnection(opts with { Name = name });
            configureConnection(conn);
            _connections[i] = conn;
        }
    }

    public INatsConnection GetConnection()
    {
        var i = Interlocked.Increment(ref _index);
        return _connections[i % _connections.Length];
    }

    public IEnumerable<INatsConnection> GetConnections()
    {
        foreach (var item in _connections)
        {
            yield return item;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in _connections)
        {
            await item.DisposeAsync().ConfigureAwait(false);
        }
    }
}
