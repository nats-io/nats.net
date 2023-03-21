namespace NATS.Client.Core;

public sealed class NatsConnectionPool : IAsyncDisposable
{
    private readonly NatsConnection[] _connections;
    private int _index = -1;

    public NatsConnectionPool()
        : this(Environment.ProcessorCount / 2, NatsOptions.Default, _ => { })
    {
    }

    public NatsConnectionPool(int poolSize)
        : this(poolSize, NatsOptions.Default, _ => { })
    {
    }

    public NatsConnectionPool(NatsOptions options)
        : this(Environment.ProcessorCount / 2, options, _ => { })
    {
    }

    public NatsConnectionPool(int poolSize, NatsOptions options)
        : this(poolSize, options, _ => { })
    {
    }

    public NatsConnectionPool(int poolSize, NatsOptions options, Action<NatsConnection> configureConnection)
    {
        poolSize = Math.Max(1, poolSize);
        _connections = new NatsConnection[poolSize];
        for (int i = 0; i < _connections.Length; i++)
        {
            var name = (options.Name == null) ? $"#{i}" : $"{options.Name}#{i}";
            var conn = new NatsConnection(options with { Name = name });
            configureConnection(conn);
            _connections[i] = conn;
        }
    }

    public NatsConnection GetConnection()
    {
        var i = Interlocked.Increment(ref _index);
        return _connections[i % _connections.Length];
    }

    public IEnumerable<NatsConnection> GetConnections()
    {
        foreach (var item in _connections)
        {
            yield return item;
        }
    }

    public INatsCommand GetCommand()
    {
        return GetConnection();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in _connections)
        {
            await item.DisposeAsync().ConfigureAwait(false);
        }
    }
}
